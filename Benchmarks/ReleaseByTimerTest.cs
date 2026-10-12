using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;

namespace sand_box.Benchmarks;

// BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
// AMD Ryzen 7 5700G with Radeon Graphics 3.80GHz, 1 CPU, 16 logical and 8 physical cores
//     .NET SDK 10.0.302
//     [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
// Job-NPNGVO : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
//
// Force=True  InvocationCount=1  UnrollFactor=1  
// | Method | Mean      | Error     | StdDev    | Ratio         | RatioSD | Completed Work Items | Lock Contentions | Gen0      | Gen1      | Gen2      | Allocated | Alloc Ratio |
// |------- |----------:|----------:|----------:|--------------:|--------:|---------------------:|-----------------:|----------:|----------:|----------:|----------:|------------:|
// | Stack  | 484.19 ms |  9.539 ms | 13.057 ms |      baseline |         |              20.0000 |        1743.0000 | 8000.0000 | 7000.0000 | 1000.0000 |  63.58 MB |             |
// | Queue  | 289.73 ms |  5.770 ms |  7.702 ms |  1.67x faster |   0.06x |              19.0000 |                - | 3000.0000 | 2000.0000 |         - |  31.24 MB |  2.04x less |
// | Local  |  52.59 ms |  6.967 ms | 20.541 ms | 10.76x faster |   4.21x |              17.0000 |                - |         - |         - |         - |   4.32 MB | 14.70x less |
// | Bag    | 442.36 ms | 13.742 ms | 40.304 ms |  1.10x faster |   0.11x |              22.0000 |       29658.0000 |         - |         - |         - |   5.08 MB | 12.51x less |

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
    private ConcurrentQueue<List<int>>[] _processorQueues;
    private readonly List<int> _processorFinishedItem = new(0);
    private readonly int _processorCount = Environment.ProcessorCount;
    private readonly int _processorOperations = 100_001;

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

        _processorQueues = new ConcurrentQueue<List<int>>[_processorCount];
        for (var i = 0; i < _processorQueues.Length; i++)
        {
            _processorQueues[i] = new ConcurrentQueue<List<int>>();
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _processorQueues = new ConcurrentQueue<List<int>>[_processorCount];
        for (var i = 0; i < _processorQueues.Length; i++)
        {
            _processorQueues[i] = new ConcurrentQueue<List<int>>();
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

            tasks[i] = Task.Run(() =>
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
            var spinner = new SpinWait();
            while (Volatile.Read(ref _generation) == initialGeneration)
            {
                if (spinner.NextSpinWillYield)
                {
                    spinner.Reset();
                }

                spinner.SpinOnce();
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