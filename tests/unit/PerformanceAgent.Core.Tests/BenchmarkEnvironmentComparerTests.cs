using PerformanceAgent.Core.Evidence;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class BenchmarkEnvironmentComparerTests
{
    [Fact]
    public void Compare_AcceptsMatchingEnvironments()
    {
        var environment = new BenchmarkEnvironment(".NET 10.0.12", "Linux", "X64");

        var result = new BenchmarkEnvironmentComparer().Compare(environment, environment);

        Assert.True(result.IsComparable);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void Compare_RejectsArchitectureMismatch()
    {
        var baseline = new BenchmarkEnvironment(".NET 10.0.12", "Linux", "X64");
        var candidate = new BenchmarkEnvironment(".NET 10.0.12", "Linux", "Arm64");

        var result = new BenchmarkEnvironmentComparer().Compare(baseline, candidate);

        Assert.False(result.IsComparable);
        Assert.Contains(result.Differences, difference => difference.Contains("architecture", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_RejectsMissingMetadata()
    {
        var result = new BenchmarkEnvironmentComparer().Compare(null, null);

        Assert.False(result.IsComparable);
        Assert.Single(result.Differences);
    }
}
