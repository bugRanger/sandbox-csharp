using BenchmarkDotNet.Attributes;

namespace sand_box.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ExceptionTests
{
    /// <summary>
    /// Запуск бенчей.
    /// </summary>
    /// <remarks>
    /// Нужен только для удобного запуска из проекта.
    /// </remarks>`
    public static void Run() =>
        _ = BenchmarkDotNet.Running.BenchmarkRunner.Run<ExceptionTests>(BenchmarkConfig.Default);

    [Params(100, 1000, 10_000)] public int Iterations { get; set; }

    [Benchmark]
    public void UseTryCatch()
    {
        for (int i = 0; i < Iterations; i++)
        {
            try
            {
                ThrowAnException();
            }
            catch (InvalidOperationException)
            {
                // Handle the exception, the time taken is measured here
            }
        }
    }

    [Benchmark]
    public void UseResultPattern()
    {
        for (int i = 0; i < Iterations; i++)
        {
            // A pattern that avoids exceptions, e.g., TryGetValue
            if (!TryGetValid(out _))
            {
                // Handle the "error" using normal control flow
            }
        }
    }

    // A method designed to throw an exception
    private static void ThrowAnException()
    {
        throw new InvalidOperationException("This is expected in the benchmark");
    }

    // A method using the "Try" pattern
    private static bool TryGetValid(out int value)
    {
        value = 0;
        return false; // Simulate failure without exception
    }
}