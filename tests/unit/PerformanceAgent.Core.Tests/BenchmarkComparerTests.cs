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
        Assert.Equal(-20, result.Mean.PercentChange!.Value, 6);
        Assert.Equal(-25, result.AllocatedBytes.PercentChange!.Value, 6);
        Assert.Equal(ComparisonStatus.Comparable, result.Mean.Status);
    }

    [Fact]
    public void Compare_RejectsDifferentBenchmarks()
    {
        var baseline = new BenchmarkMeasurement("A", 100, 100);
        var candidate = new BenchmarkMeasurement("B", 100, 100);

        Assert.Throws<ArgumentException>(() => new BenchmarkComparer().Compare(baseline, candidate));
    }

    [Fact]
    public void Compare_MarksIncreaseFromZeroAsNoBaseline()
    {
        var baseline = new BenchmarkMeasurement("A", 0, 0);
        var candidate = new BenchmarkMeasurement("A", 1, 1);

        var result = new BenchmarkComparer().Compare(baseline, candidate);

        Assert.Null(result.Mean.PercentChange);
        Assert.Null(result.AllocatedBytes.PercentChange);
        Assert.Equal(ComparisonStatus.NoBaseline, result.Mean.Status);
        Assert.Equal(ComparisonStatus.NoBaseline, result.AllocatedBytes.Status);
    }

    [Fact]
    public void Compare_TreatsZeroToZeroAsComparableWithoutChange()
    {
        var baseline = new BenchmarkMeasurement("A", 0, 0);
        var candidate = new BenchmarkMeasurement("A", 0, 0);

        var result = new BenchmarkComparer().Compare(baseline, candidate);

        Assert.Equal(0, result.Mean.PercentChange);
        Assert.Equal(ComparisonStatus.Comparable, result.Mean.Status);
    }
    [Fact]
    public void Compare_MarksMissingAllocationAsUnavailable()
    {
        var baseline = new BenchmarkMeasurement("A", 100, null);
        var candidate = new BenchmarkMeasurement("A", 90, 0);

        var result = new BenchmarkComparer().Compare(baseline, candidate);

        Assert.Equal(ComparisonStatus.Unavailable, result.AllocatedBytes.Status);
        Assert.Null(result.AllocatedBytes.Baseline);
        Assert.Equal(0, result.AllocatedBytes.Candidate);
        Assert.Null(result.AllocatedBytes.PercentChange);
    }
}
