using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class BenchmarkComparerTests
{
    [Fact]
    public void Compare_ComputesDeterministicChanges()
    {
        var baseline = new BenchmarkMeasurement("MapOrder", 100, 1_000);
        var candidate = new BenchmarkMeasurement("MapOrder", 80, 750);

        var result = new BenchmarkComparer().Compare(baseline, candidate);

        Assert.Equal("MapOrder", result.BenchmarkName);
        Assert.Equal(-20, result.Mean.PercentChange, 6);
        Assert.Equal(-25, result.AllocatedBytes.PercentChange, 6);
    }

    [Fact]
    public void Compare_RejectsDifferentBenchmarks()
    {
        var baseline = new BenchmarkMeasurement("A", 100, 100);
        var candidate = new BenchmarkMeasurement("B", 100, 100);

        Assert.Throws<ArgumentException>(() => new BenchmarkComparer().Compare(baseline, candidate));
    }

    [Fact]
    public void Compare_RepresentsIncreaseFromZeroAsInfinity()
    {
        var baseline = new BenchmarkMeasurement("A", 0, 0);
        var candidate = new BenchmarkMeasurement("A", 1, 1);

        var result = new BenchmarkComparer().Compare(baseline, candidate);

        Assert.True(double.IsPositiveInfinity(result.Mean.PercentChange));
        Assert.True(double.IsPositiveInfinity(result.AllocatedBytes.PercentChange));
    }
}
