using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace sand_box;

internal class Program
{
    private static void Main(string[] args) => BenchmarkSwitcher
        .FromAssembly(typeof(Program).Assembly)
        .Run(args, BenchmarkConfig.Default);
}

public static class BenchmarkConfig
{
    public static ManualConfig Default => ManualConfig.Create(DefaultConfig.Instance)
        .AddValidator(ExecutionValidator.FailOnError)
        .AddValidator(ReturnValueValidator.FailOnError)
        .WithOptions(ConfigOptions.JoinSummary)
        .WithOptions(ConfigOptions.DisableLogFile)
        .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));
}