using PerformanceAgent.BenchmarkDotNet;
using Xunit;

namespace PerformanceAgent.BenchmarkDotNet.Tests;

public sealed class BenchmarkExecutionExceptionTests
{
    [Fact]
    public void Message_NamesFailedBenchmarksAndLogFile()
    {
        var exception = new BenchmarkExecutionException(
            ["StringProcessingBenchmarks.BuildReport: DefaultJob"],
            "/work/BenchmarkDotNet.Artifacts/StringProcessingBenchmarks.log");

        Assert.Contains("StringProcessingBenchmarks.BuildReport: DefaultJob", exception.Message);
        Assert.Contains("/work/BenchmarkDotNet.Artifacts/StringProcessingBenchmarks.log", exception.Message);
        Assert.Contains("No evidence was written", exception.Message);
    }

    [Fact]
    public void Message_WithoutReportsOrLog_PointsToConsoleOutput()
    {
        var message = BenchmarkExecutionException.CreateMessage([], null);

        Assert.StartsWith("BenchmarkDotNet produced no benchmark reports.", message);
        Assert.Contains("BenchmarkDotNet output above", message);
    }
}
