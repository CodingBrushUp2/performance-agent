namespace PerformanceAgent.BenchmarkDotNet;

/// <summary>
/// BenchmarkDotNet finished without measurements for one or more benchmark cases,
/// typically because it could not build or run its generated boilerplate project.
/// </summary>
public sealed class BenchmarkExecutionException : Exception
{
    public BenchmarkExecutionException(IReadOnlyList<string> failedBenchmarks, string? logFilePath)
        : base(CreateMessage(failedBenchmarks, logFilePath))
    {
        FailedBenchmarks = failedBenchmarks;
        LogFilePath = logFilePath;
    }

    public IReadOnlyList<string> FailedBenchmarks { get; }

    public string? LogFilePath { get; }

    public static string CreateMessage(IReadOnlyList<string> failedBenchmarks, string? logFilePath)
    {
        ArgumentNullException.ThrowIfNull(failedBenchmarks);
        var subject = failedBenchmarks.Count == 0
            ? "BenchmarkDotNet produced no benchmark reports"
            : $"BenchmarkDotNet produced no measurements for: {string.Join(", ", failedBenchmarks)}";
        var log = string.IsNullOrWhiteSpace(logFilePath)
            ? "See the BenchmarkDotNet output above for the build or run error."
            : $"See the BenchmarkDotNet log for the build or run error: {logFilePath}";
        return $"{subject}. No evidence was written. {log}";
    }
}
