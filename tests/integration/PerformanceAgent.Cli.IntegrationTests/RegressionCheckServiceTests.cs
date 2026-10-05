using Xunit;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Verdicts;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class RegressionCheckServiceTests
{
    private static readonly BenchmarkEnvironment LinuxEnvironment =
        new(".NET 10", "Linux", "X64");

    [Fact]
    public void Missing_required_allocation_is_inconclusive()
    {
        var result = Check(
            new BenchmarkMeasurement("Sample.Work", 100, 64),
            new BenchmarkMeasurement("Sample.Work", 101, null),
            new PerformanceBudget(5, 10));

        Assert.Equal(PerformanceVerdict.Inconclusive, result.Verdict);
        Assert.Equal(PerformanceVerdict.Inconclusive, Assert.Single(result.Benchmarks).Verdict);
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("required allocation metric is not comparable (Unavailable)", StringComparison.Ordinal));
    }

    [Fact]
    public void Zero_required_baseline_is_inconclusive()
    {
        var result = Check(
            new BenchmarkMeasurement("Sample.Work", 0, 64),
            new BenchmarkMeasurement("Sample.Work", 1, 64),
            new PerformanceBudget(5, null));

        Assert.Equal(PerformanceVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("required mean metric is not comparable (NoBaseline)", StringComparison.Ordinal));
    }

    [Fact]
    public void Environment_mismatch_is_inconclusive()
    {
        var baseline = Evidence(
            new BenchmarkMeasurement("Sample.Work", 100, 64),
            LinuxEnvironment);
        var candidate = Evidence(
            new BenchmarkMeasurement("Sample.Work", 101, 64),
            new BenchmarkEnvironment(".NET 10", "Windows", "X64"));

        var result = new RegressionCheckService().Check(
            baseline,
            candidate,
            new PerformanceBudget(5, 10));

        Assert.Equal(PerformanceVerdict.Inconclusive, result.Verdict);
        Assert.Empty(result.Benchmarks);
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("operating system", StringComparison.Ordinal));
    }

    [Fact]
    public void Definite_budget_failure_wins_over_an_unavailable_metric()
    {
        var result = Check(
            new BenchmarkMeasurement("Sample.Work", 100, null),
            new BenchmarkMeasurement("Sample.Work", 120, null),
            new PerformanceBudget(5, 10));

        Assert.Equal(PerformanceVerdict.Fail, result.Verdict);
        Assert.Equal(PerformanceVerdict.Fail, Assert.Single(result.Benchmarks).Verdict);
    }

    [Fact]
    public void Unavailable_optional_metric_does_not_block_pass()
    {
        var result = Check(
            new BenchmarkMeasurement("Sample.Work", 100, null),
            new BenchmarkMeasurement("Sample.Work", 101, null),
            new PerformanceBudget(5, null));

        Assert.Equal(PerformanceVerdict.Pass, result.Verdict);
        Assert.Equal(PerformanceVerdict.Pass, Assert.Single(result.Benchmarks).Verdict);
        Assert.Empty(result.Reasons);
    }

    private static EvidenceCheckResult Check(
        BenchmarkMeasurement baseline,
        BenchmarkMeasurement candidate,
        PerformanceBudget budget) =>
        new RegressionCheckService().Check(
            Evidence(baseline, LinuxEnvironment),
            Evidence(candidate, LinuxEnvironment),
            budget);

    private static BenchmarkEvidence Evidence(
        BenchmarkMeasurement measurement,
        BenchmarkEnvironment environment) =>
        new("1.0", [measurement], environment);
}
