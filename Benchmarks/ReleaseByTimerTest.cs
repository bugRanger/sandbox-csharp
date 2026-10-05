using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;

namespace sand_box.Benchmarks;


// BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
// AMD Ryzen 7 5700G with Radeon Graphics 3.80GHz, 1 CPU, 16 logical and 8 physical cores
//     .NET SDK 10.0.302
//     [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
// Job-NPNGVO : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
//
// Force=True  InvocationCount=1  UnrollFactor=1  
//
// | Method | Mean      | Error    | StdDev   | Ratio | Gen0      | Completed Work Items | Lock Contentions | Gen1      | Allocated | Alloc Ratio |
// |------- |----------:|---------:|---------:|------:|----------:|---------------------:|-----------------:|----------:|----------:|------------:|
// | Stack  | 523.64 ms | 6.199 ms | 5.495 ms |  1.00 | 6000.0000 |              19.0000 |        2475.0000 | 5000.0000 |  58.52 MB |        1.00 |
// | Queue  | 273.63 ms | 4.764 ms | 4.457 ms |  0.52 |         - |              18.0000 |                - |         - |   5.96 MB |        0.10 |
// | Local  |  94.81 ms | 1.811 ms | 3.819 ms |  0.18 |         - |              18.0000 |                - |         - |   6.68 MB |        0.11 |

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
    private TestQueue<List<int>> _queue;
    private TestLocal<List<int>> _local;
    private ConcurrentQueue<List<int>>[] _processorQueues;
    private readonly List<int> _processorFinishedItem = new(0);
    private readonly int _processorCount = Environment.ProcessorCount;
    private readonly int _processorOperations = 100_001;

    public static void Run() => BenchmarkDotNet.Running.BenchmarkRunner.Run<ReleaseByTimerTest>();

    [GlobalSetup]
    public void Setup()
    {
        if (!ThreadPool.SetMaxThreads(_processorCount, _processorCount))
        {
            throw new Exception("SetMaxThreads failed");
        }

        var factoryFn = () => new List<int>(101);
        _stack = new TestStack<List<int>>(factoryFn);
        _queue = new TestQueue<List<int>>(factoryFn, _processorOperations);
        _local = new TestLocal<List<int>>(factoryFn, _processorOperations);

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

    [Benchmark(Baseline = true)] public long Stack() => Measure(_stack);
    [Benchmark] public long Queue() => Measure(_queue);
    [Benchmark] public long Local() => Measure(_local);

    private long Measure(ITestImpl<List<int>> testImpl)
    {
        ArgumentNullException.ThrowIfNull(testImpl);

        var tasks = new Task<int>[_processorCount];
        var barrier = new SpinBarrier(_processorCount + 1);

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

        barrier.SignalAndWait();

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
                spinner.SpinOnce();
            }
        }
    }
}

internal interface ITestImpl<T> where T : class
{
    T Acquire();
    void Release(T item);
}

internal class TestLocal<T>(Func<T> factoryFn, int boundedCapacity) : ITestImpl<T> where T : class
{
    private readonly ThreadLocal<Queue<T>> _local = new(() => new Queue<T>(), false);
    private readonly ConcurrentQueue<T> _global = new();
    private int _queueCounter;
    private int _acquireCounter;

    public T Acquire()
    {
        if (!_local.Value!.TryDequeue(out var item))
        {
            if (!_global.TryDequeue(out item))
            {
                return factoryFn();
            }
        }

        if (Interlocked.Increment(ref _acquireCounter) >= boundedCapacity)
        {
            Interlocked.Decrement(ref _acquireCounter);
        }

        Interlocked.Decrement(ref _queueCounter);
        return item;
    }

    public void Release(T item)
    {
        if (Interlocked.Decrement(ref _acquireCounter) < 0)
        {
            Interlocked.Increment(ref _acquireCounter);
        }
        else
        {
            _local.Value!.Enqueue(item);
            return;
        }

        if (Interlocked.Increment(ref _queueCounter) < boundedCapacity)
        {
            _global.Enqueue(item);
        }
        else
        {
            Interlocked.Decrement(ref _queueCounter);
        }
    }
}

internal class TestQueue<T>(Func<T> factoryFn, int boundedCapacity) : ITestImpl<T> where T : class
{
    private int _count;
    private readonly ConcurrentQueue<T> _queue = new();

    public T Acquire()
    {
        if (!_queue.TryDequeue(out var item))
        {
            return factoryFn();
        }

        Interlocked.Decrement(ref _count);
        return item;
    }

    public void Release(T item)
    {
        if (Interlocked.Increment(ref _count) >= boundedCapacity)
        {
            Interlocked.Decrement(ref _count);
            return;
        }

        _queue.Enqueue(item);
    }
}

internal class TestStack<T> : ITestImpl<T> where T : class
{
    private readonly Stack<T> _inner = new();
    private readonly Func<T> _factoryFn;
    private readonly Timer _timer;

    public TestStack(Func<T> factoryFn)
    {
        _factoryFn = factoryFn;
        _timer = new Timer(Resize, null, 100, 100);
    }

    public T Acquire()
    {
        lock (_inner)
        {
            return _inner.Count == 0 ? _factoryFn() : _inner.Pop();
        }
    }

    public void Release(T item)
    {
        lock (_inner)
        {
            _inner.Push(item);
        }
    }

    private void Resize(object? state)
    {
        lock (_inner)
        {
            var removed = _inner.Count / 2;
            while (removed > 0)
            {
                removed--;
                _inner.TryPop(out _);
            }
        }
    }
}