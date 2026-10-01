using PerformanceAgent.Core.Calibration;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class CalibrationAnalyzerTests
{
    private static readonly BenchmarkEnvironment Environment = new(".NET 10", "Linux", "X64");

    [Fact]
    public void Analyze_StableSamples_ReturnsMedianAndStableResult()
    {
        var samples = new[]
        {
            Evidence(100, 1000),
            Evidence(102, 1000),
            Evidence(101, 1000)
        };

        var result = new CalibrationAnalyzer().Analyze(samples, 5);

        Assert.True(result.IsStable);
        var metric = Assert.Single(result.Metrics);
        Assert.Equal(101, metric.MedianMeanNanoseconds);
        Assert.Equal(1000, metric.MedianAllocatedBytesPerOperation);
        Assert.Empty(result.InstabilityReasons);
    }

    [Fact]
    public void Analyze_NoisySamples_ExplainsInstability()
    {
        var samples = new[] { Evidence(100, 1000), Evidence(120, 1300), Evidence(101, 1000) };

        var result = new CalibrationAnalyzer().Analyze(samples, 5);

        Assert.False(result.IsStable);
        Assert.Contains(result.InstabilityReasons, reason => reason.Contains("mean spread", StringComparison.Ordinal));
        Assert.Contains(result.InstabilityReasons, reason => reason.Contains("allocation spread", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RejectsDifferentBenchmarkSets()
    {
        var first = Evidence(100, 1000);
        var second = new BenchmarkEvidence("1.0", [new BenchmarkMeasurement("Other", 100, 1000)], Environment);

        Assert.Throws<InvalidOperationException>(() => new CalibrationAnalyzer().Analyze([first, second], 5));
    }

    [Fact]
    public void Analyze_RejectsDifferentEnvironments()
    {
        var first = Evidence(100, 1000);
        var second = new BenchmarkEvidence("1.0", [new BenchmarkMeasurement("Bench", 100, 1000)], new BenchmarkEnvironment(".NET 10", "Windows", "X64"));

        Assert.Throws<InvalidOperationException>(() => new CalibrationAnalyzer().Analyze([first, second], 5));
    }

    [Fact]
    public void Analyze_RequiresAtLeastTwoSamples()
    {
        Assert.Throws<ArgumentException>(() => new CalibrationAnalyzer().Analyze([Evidence(100, 1000)], 5));
    }

    private static BenchmarkEvidence Evidence(double mean, long? allocated) =>
        new("1.0", [new BenchmarkMeasurement("Bench", mean, allocated)], Environment);
}
