using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace sand_box.Benchmarks;

// BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
// AMD Ryzen 7 5700G with Radeon Graphics 3.80GHz, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 10.0.302
//   [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
//   Job-NPNGVO : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
//
// Force=True  InvocationCount=1  UnrollFactor=1  
//
// | Method  | Mean       | Error    | StdDev    | Ratio        | RatioSD | Completed Work Items | Lock Contentions | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
// |-------- |-----------:|---------:|----------:|-------------:|--------:|---------------------:|-----------------:|-----------:|-----------:|----------:|----------:|------------:|
// | Stack   | 5,216.2 ms | 42.42 ms |  37.61 ms |     baseline |         |              28.0000 |       20848.0000 | 26000.0000 | 25000.0000 | 1000.0000 | 197.41 MB |             |
// | Bag     | 4,189.9 ms | 83.56 ms | 158.98 ms | 1.25x faster |   0.05x |              31.0000 |      337638.0000 |  7000.0000 |  6000.0000 | 1000.0000 |  48.48 MB |  4.07x less |
// | Queue   | 2,941.2 ms | 14.95 ms |  12.49 ms | 1.77x faster |   0.01x |              22.0000 |                - | 19000.0000 | 18000.0000 | 2000.0000 | 134.51 MB |  1.47x less |
// | Local   |   664.5 ms | 34.14 ms | 100.67 ms | 8.04x faster |   1.28x |              31.0000 |                - | 48000.0000 | 47000.0000 | 2000.0000 | 359.83 MB |  1.82x more |
// | LocalV1 |   655.6 ms | 30.93 ms |  91.21 ms | 8.11x faster |   1.16x |              32.0000 |                - | 56000.0000 | 55000.0000 | 1000.0000 | 429.58 MB |  2.18x more |


/// <summary>
/// Решение проблемы с отслеживанием памяти высвобождаемой в тике таймера.
/// Для избежания потери данных по GC применяется ограничение на кол-во потоков <see cref="ThreadPool.SetMaxThreads"/>,
/// которое вынуждает код замера и тики таймера выполнятся на смежных <see cref="System.Threading.Thread"/>.
/// </summary>
/// <remarks>
/// Потери данных по высвобождаемой памяти происходят если тик таймера попадает на поток который не был задействован в замере.
/// </remarks>
[GcForce]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[ReturnValueValidator(true)]
public class ReleaseByTimerTest
{
    private TestStack<List<int>> _stack;
    private TestBag<List<int>> _bag;
    private TestQueue<List<int>> _queue;
    private TestLocal<List<int>> _local;
    private ConcurrentPool<List<int>> _localV1;
    private BoundedQueue<List<int>>[] _processorQueues;
    private readonly List<int> _processorFinishedItem = new(0);
    private readonly int _processorCount = Environment.ProcessorCount;
    private readonly int _processorOperations = 1_000_001;

    public static void Run() =>
        BenchmarkDotNet.Running.BenchmarkRunner.Run<ReleaseByTimerTest>(BenchmarkConfig.Default);

    [GlobalSetup]
    public void Setup()
    {
        if (!ThreadPool.SetMaxThreads(_processorCount, _processorCount))
        {
            throw new Exception("SetMaxThreads failed");
        }

        var factoryFn = () => new List<int>(101);
        _stack = new TestStack<List<int>>(factoryFn, 1, TimeSpan.FromMilliseconds(1));
        _bag = new TestBag<List<int>>(factoryFn, 1024);
        _queue = new TestQueue<List<int>>(factoryFn, 1024);
        _local = new TestLocal<List<int>>(factoryFn, 1024);
        _localV1 = new ConcurrentPool<List<int>>(factoryFn, 1024);

        _processorQueues = new BoundedQueue<List<int>>[_processorCount];
        for (var i = 0; i < _processorQueues.Length; i++)
        {
            _processorQueues[i] = new BoundedQueue<List<int>>(_processorOperations);
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _processorQueues = new BoundedQueue<List<int>>[_processorCount];
        for (var i = 0; i < _processorQueues.Length; i++)
        {
            _processorQueues[i] = new BoundedQueue<List<int>>(_processorOperations);
        }
    }

    [Benchmark(Baseline = true)]
    public long Stack() => Measure(_stack);

    [Benchmark]
    public long Bag() => Measure(_bag);

    [Benchmark]
    public long Queue() => Measure(_queue);

    [Benchmark]
    public long Local() => Measure(_local);

    [Benchmark]
    public long LocalV1() => Measure(_localV1);

    private long Measure(ITestImpl<List<int>> testImpl)
    {
        ArgumentNullException.ThrowIfNull(testImpl);

        var tasks = new Task<int>[_processorCount];
        var barrier = new SpinBarrier(_processorCount);

        for (var i = 0; i < _processorCount; i++)
        {
            var dequeueQueue = _processorQueues[i];
            var enqueueQueue = _processorQueues[_processorQueues.Length - i - 1];

            tasks[i] = Task.Factory.StartNew(() =>
            {
                barrier.SignalAndWait();

                var finished = false;
                var itemCounter = 0;
                for (var j = 0; j < _processorOperations; j++)
                {
                    var item = testImpl.Acquire();
                    for (var k = 0; k < item.Capacity; k++)
                    {
                        item.Add(k);
                    }

                    enqueueQueue.Enqueue(item);

                    if (dequeueQueue.TryDequeue(out item))
                    {
                        finished = item == _processorFinishedItem;
                        if (finished)
                        {
                            continue;
                        }

                        itemCounter += item.Count;
                        item.Clear();
                        testImpl.Release(item);
                    }
                }

                enqueueQueue.Enqueue(_processorFinishedItem);

                while (!finished)
                {
                    if (!dequeueQueue.TryDequeue(out var item))
                    {
                        continue;
                    }

                    finished = item == _processorFinishedItem;
                    if (finished)
                    {
                        continue;
                    }

                    itemCounter += item.Count;
                    item.Clear();
                    testImpl.Release(item);
                }

                return itemCounter;
            });
        }

        return Task
            .WhenAll(tasks)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult()
            .Sum();
    }
}

internal class SpinBarrier(int participantCount)
{
    private readonly int _participantCount = participantCount;
    private int _remaining = participantCount;
    private int _generation;

    public void SignalAndWait()
    {
        var initialGeneration = Volatile.Read(ref _generation);
        if (Interlocked.Decrement(ref _remaining) == 0)
        {
            _remaining = _participantCount;
            Interlocked.Increment(ref _generation);
        }
        else
        {
            while (Volatile.Read(ref _generation) == initialGeneration)
            {
                Thread.SpinWait(10);
            }
        }
    }
}

internal class ConcurrentPool<T>(Func<T> factoryFn, int boundedCapacity) : ITestImpl<T> where T : class
{
    private readonly ThreadLocal<uint> _acquireCounter = new(() => 0, false);
    private readonly ThreadLocal<Queue<T>> _local = new(() => new Queue<T>(), false);
    private readonly ConcurrentBoundedCollection<T> _global = new(new ConcurrentQueue<T>(), boundedCapacity);

    public T Acquire()
    {
        if (_acquireCounter.Value < boundedCapacity)
        {
            _acquireCounter.Value++;
        }

        if (_local.Value!.TryDequeue(out var item))
        {
            return item;
        }

        return _global.TryDequeue(out item) ? item : factoryFn();
    }

    public void Release(T item)
    {
        if (_acquireCounter.Value > 0)
        {
            _acquireCounter.Value--;

            if (_local.Value!.Count < boundedCapacity)
            {
                _local.Value.Enqueue(item);
                return;
            }
        }

        _global.Enqueue(item);
    }
}

internal interface ITestImpl<T> where T : class
{
    T Acquire();

    void Release(T item);
}

internal class TestBag<T>(Func<T> factoryFn, int boundedCapacity) : ITestImpl<T> where T : class
{
    private readonly ConcurrentBoundedCollection<T> _queue = new(new ConcurrentBag<T>(), boundedCapacity);

    public T Acquire()
    {
        return _queue.TryDequeue(out var item) ? item : factoryFn();
    }

    public void Release(T item)
    {
        _queue.Enqueue(item);
    }
}

internal class TestLocal<T>(Func<T> factoryFn, int boundedCapacity) : ITestImpl<T> where T : class
{
    private readonly ThreadLocal<Queue<T>> _local = new(() => new Queue<T>(), false);
    private readonly ConcurrentBoundedCollection<T> _global = new(new ConcurrentQueue<T>(), boundedCapacity);

    public T Acquire()
    {
        if (_local.Value!.TryDequeue(out var item))
        {
            return item;
        }

        return _global.TryDequeue(out item) ? item : factoryFn();
    }

    public void Release(T item)
    {
        if (_local.Value!.Count < boundedCapacity)
        {
            _local.Value!.Enqueue(item);
            return;
        }

        _global.Enqueue(item);
    }
}

internal class TestQueue<T>(Func<T> factoryFn, int boundedCapacity) : ITestImpl<T> where T : class
{
    private readonly ConcurrentBoundedCollection<T> _boundedCollection = new(new ConcurrentQueue<T>(), boundedCapacity);

    public T Acquire()
    {
        return _boundedCollection.TryDequeue(out var item) ? item : factoryFn();
    }

    public void Release(T item)
    {
        _boundedCollection.Enqueue(item);
    }
}

internal class TestStack<T> : ITestImpl<T> where T : class
{
    private const int MinCapacity = 1;

    private readonly Stack<T> _inner = new();
    private readonly Func<T> _factoryFn;
    private readonly Timer _timer;
    private readonly int _maxTrimSlices;
    private int _nextTrimSlices;
    private int _minSize = int.MinValue;
    private int _maxSize = int.MaxValue;

    public TestStack(Func<T> factoryFn, int trimSlices, TimeSpan period)
    {
        _factoryFn = factoryFn;
        _maxTrimSlices = trimSlices;
        _nextTrimSlices = trimSlices;
        _timer = new Timer(Resize, null, period, period);
    }

    public T Acquire()
    {
        lock (_inner) return _inner.Count == 0 ? _factoryFn() : _inner.Pop();
    }

    public void Release(T item)
    {
        lock (_inner) _inner.Push(item);
    }

    private void Resize(object? state)
    {
        if (_maxSize < _inner.Count) _maxSize = _inner.Count;
        if (_minSize > _inner.Count) _minSize = _inner.Count;

        _nextTrimSlices--;
        if (_nextTrimSlices > 0) return;
        _nextTrimSlices = _maxTrimSlices;

        var workingSize = _maxSize - _minSize;
        var needKeep = Math.Max((int)(workingSize * 1.25) + 1, (int)(_minSize * 0.4));
        if (needKeep < MinCapacity) needKeep = MinCapacity;

        lock (_inner)
        {
            if (_inner.Count <= needKeep) return;

            while (_inner.Count > needKeep)
            {
                _inner.Pop();
            }
        }
    }
}

public class ConcurrentBoundedCollection<T>(IProducerConsumerCollection<T> collection, int boundedCapacity)
{
    private int _counter;

    public bool TryDequeue([MaybeNullWhen(false)] out T item)
    {
        if (!collection.TryTake(out item))
        {
            return false;
        }

        Interlocked.Decrement(ref _counter);
        return true;
    }

    public void Enqueue(T item)
    {
        if (Interlocked.Increment(ref _counter) >= boundedCapacity)
        {
            Interlocked.Decrement(ref _counter);
            return;
        }

        _ = collection.TryAdd(item);
    }
}

/// Выравниваем структуру по размеру кэш-линии процессора (64 байта),
/// чтобы операции записи одного потока не инвалидировали кэш другого.
[StructLayout(LayoutKind.Sequential)]
public class BoundedQueue<T>(int boundedCapacity)
    where T : class
{
    // --- КЭШ-ЛИНИЯ 1: Данные конфигурации очереди ---
    private readonly T?[] _buffer = new T[boundedCapacity];

    // Padding: в 56 байт (7 полей по 8 байт), чтобы отделить конфигурацию от указателя записи.
    private readonly long _p1, _p2, _p3, _p4, _p5, _p6, _p7;

    // --- КЭШ-ЛИНИЯ 2: Указатель хвоста (Использует только Писатель) ---
    private long _enqueuePos;

    // Padding: в 56 байт (7 полей по 8 байт), чтобы гарантированно изолировать _enqueuePos.
    private readonly long _p8, _p9, _p10, _p11, _p12, _p13, _p14;

    // --- КЭШ-ЛИНИЯ 3: Указатель головы (Использует только Читатель) ---
    private long _dequeuePos;

    /// <summary>
    /// Проверяет, закрыта ли очередь и вычитаны ли из неё абсолютно все элементы.
    /// </summary>
    public bool IsCompleted => Volatile.Read(ref _dequeuePos) == _buffer.Length;

    public void Enqueue(T item)
    {
        var currentTail = _enqueuePos;
        var currentHead = Volatile.Read(ref _dequeuePos);

        if (currentTail - currentHead >= boundedCapacity)
        {
            return;
        }

        var index = (int)(currentTail % boundedCapacity);

        Volatile.Write(ref _buffer[index], item);
        Volatile.Write(ref _enqueuePos, currentTail + 1);
    }

    public bool TryDequeue(out T? item)
    {
        var currentHead = _dequeuePos;
        var currentTail = Volatile.Read(ref _enqueuePos);

        if (currentHead == currentTail)
        {
            item = null;
            return false;
        }

        var index = (int)(currentHead % boundedCapacity);
        while (true)
        {
            item = Volatile.Read(ref _buffer[index]);
            if (item != null)
            {
                break;
            }
        }

        Volatile.Write(ref _buffer[index], null);
        Volatile.Write(ref _dequeuePos, currentHead + 1);
        return true;
    }
}