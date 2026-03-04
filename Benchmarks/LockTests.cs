using BenchmarkDotNet.Attributes;

namespace sand_box.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class LockTests
{
    /// <summary>
    /// Запуск бенчей.
    /// </summary>
    /// <remarks>
    /// Нужен только для удобного запуска из проекта.
    /// </remarks>`
    public static void Run() =>
        _ = BenchmarkDotNet.Running.BenchmarkRunner.Run<LockTests>(BenchmarkConfig.Default);

    private long[] _numbers = [];
    private readonly LockSimple _lockSimple = new();
    private readonly LockPrecondition _lockPrecondition = new();
    private readonly LockFreeValue _lockFreeValue = new();
    private readonly LockFreeSection _lockFreeSection = new();
    private readonly LockSpin _lockSpin = new();
    private readonly LockSpinCustom _lockSpinCustom = new();
    private readonly LockSpinWait _lockSpinWait = new();

    [Params(1, 2, 4, 8)] public int Parallelism { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        var lastNumber = _numbers.Length > 0 ? _numbers[^1] : 0;
        _numbers = Enumerable.Range(1, 1_000_000).Select(idx => lastNumber + idx).ToArray();
    }

    [Benchmark(Baseline = true)]
    public long LockSimple() => Processing(_lockSimple);

    [Benchmark]
    public long LockSimpleCopy1() => Processing(_lockSimple);

    [Benchmark]
    public long LockSimpleCopy2() => Processing(_lockSimple);

    [Benchmark]
    public long LockPrecondition() => Processing(_lockPrecondition);

    [Benchmark]
    public long LockFreeValue() => Processing(_lockFreeValue);

    [Benchmark]
    public long LockFreeSection() => Processing(_lockFreeSection);

    [Benchmark]
    public long LockSpin() => Processing(_lockSpin);

    [Benchmark]
    public long LockSpinCustom() => Processing(_lockSpinCustom);

    [Benchmark]
    public long LockSpinWait() => Processing(_lockSpinWait);

    private long Processing<T>(T counter)
        where T : IIncrementor
    {
        Parallel.For(0, Parallelism, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, _ =>
        {
            foreach (var number in _numbers)
            {
                counter.Increment(number);
            }
        });

        return counter.GetNumber();
    }
}

public interface IIncrementor
{
    long GetNumber();

    void Increment(long number);
}

public class LockSimple : IIncrementor
{
    private readonly Lock _lock = new();
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        lock (_lock)
        {
            if (_number <= number)
            {
                return;
            }

            _number = number;
        }
    }
}

public class LockPrecondition : IIncrementor
{
    private readonly Lock _lock = new();
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        if (number <= Volatile.Read(ref _number))
        {
            return;
        }

        lock (_lock)
        {
            if (number <= _number)
            {
                return;
            }

            Volatile.Write(ref _number, number);
        }
    }
}

public class LockFreeValue : IIncrementor
{
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        while (true)
        {
            var current = Volatile.Read(ref _number);
            if (number <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _number, number, current) == current)
            {
                return;
            }
        }
    }
}

public class LockFreeSection : IIncrementor
{
    private bool _block;
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        while (true)
        {
            var current = Volatile.Read(ref _number);
            if (number <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _block, true, false))
            {
                continue;
            }

            break;
        }

        try
        {
            Volatile.Write(ref _number, number);
        }
        finally
        {
            Interlocked.Exchange(ref _block, false);
        }
    }
}

public class LockSpin : IIncrementor
{
    private SpinLock _lock = new();
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        var lockTaken = false;

        while (true)
        {
            var current = Volatile.Read(ref _number);
            if (number <= current)
            {
                return;
            }

            _lock.TryEnter(ref lockTaken);
            if (lockTaken)
            {
                break;
            }
        }

        try
        {
            Volatile.Write(ref _number, number);
        }
        finally
        {
            _lock.Exit(true);
        }
    }
}

public class LockSpinCustom : IIncrementor
{
    private readonly Lock _lock = new();
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        while (true)
        {
            var current = Volatile.Read(ref _number);
            if (number <= current)
            {
                return;
            }

            if (_lock.TryEnter(TimeSpan.FromTicks(10)))
            {
                break;
            }
        }

        try
        {
            Volatile.Write(ref _number, number);
        }
        finally
        {
            _lock.Exit();
        }
    }
}

public class LockSpinWait : IIncrementor
{
    private bool _block;
    private long _number;

    public long GetNumber() => _number;

    public void Increment(long number)
    {
        var spinner = new SpinWait();
        while (true)
        {
            var current = Volatile.Read(ref _number);
            if (number <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _block, true, false))
            {
                spinner.SpinOnce();
                continue;
            }

            break;
        }

        try
        {
            Volatile.Write(ref _number, number);
        }
        finally
        {
            Interlocked.Exchange(ref _block, false);
        }
    }
}