using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace sand_box.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class DataStreamTests
{
    /// <summary>
    /// Запуск бенчей.
    /// </summary>
    /// <remarks>
    /// Нужен только для удобного запуска из проекта.
    /// </remarks>`
    public static void Run() =>
        _ = BenchmarkDotNet.Running.BenchmarkRunner.Run<DataStreamTests>(BenchmarkConfig.Default);

    private readonly LockStreamHandler _lockStreamHandler = new();
    private readonly ChannelStreamHandler _channelStreamHandler = new(1_000);

    private Data[] _updates = [];

    [Params(1, 2, 4, 8)] public int Parallelism { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _updates = Enumerable.Range(1, 100_001)
            .Select(idx => new Data
            {
                Id = idx,
                Value = 100.0,
            })
            .ToArray();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        var lastId = _updates.Last().Id;
        for (var i = 0; i < _updates.Length; i++)
        {
            _updates[i].Id = ++lastId;
        }
    }

    [Benchmark(Baseline = true)]
    public long SyncWithLock() => Processing(_lockStreamHandler);

    [Benchmark]
    public long SyncWithChannel() => Processing(_channelStreamHandler);

    private long Processing<T>(T streamHandler) where T : IStreamHandler
    {
        long totalUpdates = 0;

        Parallel.For(0, Parallelism, new ParallelOptions { MaxDegreeOfParallelism = Parallelism },
            _ => Interlocked.Add(ref totalUpdates, streamHandler.Handle(_updates)));

        return totalUpdates;
    }
}

public struct Data
{
    public long Id { get; set; }
    public double Value { get; set; }
}

public interface IStreamHandler
{
    event Action<ReadOnlySpan<Data>> OnUpdate;
    int Handle(ReadOnlySpan<Data> items);
}

public sealed class LockStreamHandler : IStreamHandler
{
    private readonly Lock _locker = new();
    private long _lastUpdateId;

    public event Action<ReadOnlySpan<Data>>? OnUpdate;

    public int Handle(ReadOnlySpan<Data> items)
    {
        lock (_locker)
        {
            if (items[^1].Id <= _lastUpdateId)
            {
                return 0;
            }

            var skipCount = 0;
            while (skipCount < items.Length && items[skipCount].Id <= _lastUpdateId)
            {
                skipCount++;
            }

            _lastUpdateId = items[^1].Id;
            OnUpdate?.Invoke(items[skipCount..]);
            return items.Length;
        }
    }
}

public class ChannelStreamHandler : IStreamHandler, IDisposable
{
    // 1. Создаем структуру для индекса с отступами (Padding)
    // 64 байта (размер кэш-линии) = 8 байт (long value) + 56 байт (отступ)
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct CacheLineAlignedCounter
    {
        [FieldOffset(0)] public long Value;
    }

    private readonly Data[] _buffer;
    private readonly int _mask;
    private readonly int _capacity;
    private readonly int _readBatchSize;

    private CacheLineAlignedCounter _writeCounter;
    private CacheLineAlignedCounter _readCounter;
    private CacheLineAlignedCounter _updateIdCounter;

    private readonly Thread _workerThread;
    private volatile bool _isRunning = true;

    public event Action<ReadOnlySpan<Data>>? OnUpdate;

    public ChannelStreamHandler(int capacity, int readBatchSize = 128)
    {
        if ((capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentException("Размер буфера должен быть степенью двойки (2^n).");
        }

        _buffer = new Data[capacity];
        _mask = capacity - 1;
        _capacity = capacity;
        _readBatchSize = readBatchSize;

        _writeCounter = default;
        _readCounter = default;
        _updateIdCounter = default;

        _workerThread = new Thread(ReaderLoop)
        {
            IsBackground = true,
            Name = "RingBuffer-Hot-Reader",
            Priority = ThreadPriority.Highest,
        };

        _workerThread.Start();
    }

    public int Handle(ReadOnlySpan<Data> items)
    {
        return WriteBatch(items);
    }

    public int WriteBatch(ReadOnlySpan<Data> batch)
    {
        var batchSize = batch.Length;
        if (batchSize == 0)
        {
            return 0;
        }

        if (batchSize > _capacity)
        {
            // TODO: Записать только вмещаемый слайс от новой позиции до конца массива.
            throw new ArgumentException("Размер батча не может превышать полную емкость буфера.");
        }

        var nextId = batch[batchSize - 1].Id;
        var skipCount = 0;

        while (true)
        {
            var currentId = Volatile.Read(ref _updateIdCounter.Value);
            if (nextId <= currentId)
            {
                return 0;
            }

            while (skipCount < batchSize && batch[skipCount].Id <= currentId)
            {
                skipCount++;
            }

            if (Interlocked.CompareExchange(ref _updateIdCounter.Value, nextId, currentId) ==
                currentId)
            {
                break;
            }
        }

        var actualBatch = batch[skipCount..];
        batchSize = actualBatch.Length;

        var endWrite = Interlocked.Add(ref _writeCounter.Value, batchSize);
        var startWrite = endWrite - batchSize;

        var startIndex = (int)(startWrite & _mask);
        var endIndex = (int)((endWrite - 1) & _mask);

        if (startIndex <= endIndex)
        {
            // Данные помещаются в массиве линейно
            batch.CopyTo(_buffer.AsSpan(startIndex, batchSize));
        }
        else
        {
            // Данные разорваны границей кольцевого массива
            var firstPartSize = _capacity - startIndex;
            batch.Slice(0, firstPartSize).CopyTo(_buffer.AsSpan(startIndex, firstPartSize));

            var secondPartSize = batchSize - firstPartSize;
            batch.Slice(firstPartSize, secondPartSize).CopyTo(_buffer.AsSpan(0, secondPartSize));
        }

        var currentRead = Volatile.Read(ref _readCounter.Value);
        if (endWrite - currentRead > _capacity)
        {
            Interlocked.Exchange(ref _readCounter.Value, endWrite - _capacity);
        }

        return batchSize;
    }

    public int TryReadBatch(Span<Data> destination)
    {
        var maxToRead = destination.Length;
        if (maxToRead == 0)
        {
            return 0;
        }

        while (true)
        {
            var currentRead = Volatile.Read(ref _readCounter.Value);
            var currentWrite = Volatile.Read(ref _writeCounter.Value);
            if (currentRead >= currentWrite)
            {
                return 0;
            }

            var available = currentWrite - currentRead;
            var actualToRead = (int)Math.Min(maxToRead, available);

            if (actualToRead == 0)
            {
                return 0;
            }

            var nextRead = currentRead + actualToRead;
            if (Interlocked.CompareExchange(ref _readCounter.Value, nextRead, currentRead) != currentRead)
            {
                continue;
            }

            var startIndex = (int)(currentRead & _mask);
            var endIndex = (int)((nextRead - 1) & _mask);

            if (startIndex <= endIndex)
            {
                // Данные лежат в массиве линейно
                _buffer.AsSpan(startIndex, actualToRead).CopyTo(destination.Slice(0, actualToRead));
            }
            else
            {
                // Данные разорваны границей кольцевого массива
                var firstPartSize = _capacity - startIndex;
                _buffer.AsSpan(startIndex, firstPartSize).CopyTo(destination.Slice(0, firstPartSize));

                var secondPartSize = actualToRead - firstPartSize;
                _buffer.AsSpan(0, secondPartSize).CopyTo(destination.Slice(firstPartSize, secondPartSize));
            }

            return actualToRead;
        }
    }

    private void ReaderLoop()
    {
        Span<Data> items = stackalloc Data[_readBatchSize];

        var spinner = new SpinWait();

        while (_isRunning)
        {
            var readCount = TryReadBatch(items);
            if (readCount > 0)
            {
                spinner.Reset();
                OnUpdate?.Invoke(items[..readCount]);
            }
            else
            {
                spinner.SpinOnce();
            }
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        _workerThread.Join();
    }
}