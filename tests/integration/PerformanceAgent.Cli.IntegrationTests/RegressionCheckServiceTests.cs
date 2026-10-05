using Xunit;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Quality;
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
            Measurement(100, 64),
            Measurement(101, null),
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
            Measurement(0, 64, 0, 0),
            Measurement(1, 64, 0.9, 1.1),
            new PerformanceBudget(5, null));

        Assert.Equal(PerformanceVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("required mean metric is not comparable (NoBaseline)", StringComparison.Ordinal));
    }

    [Fact]
    public void Environment_mismatch_is_inconclusive()
    {
        var baseline = Evidence(
            Measurement(100, 64),
            LinuxEnvironment);
        var candidate = Evidence(
            Measurement(101, 64),
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
    public void Definite_mean_budget_failure_wins_over_an_unavailable_allocation()
    {
        var result = Check(
            Measurement(100, null),
            Measurement(120, null),
            new PerformanceBudget(5, 10));

        var benchmark = Assert.Single(result.Benchmarks);
        Assert.Equal(PerformanceVerdict.Fail, result.Verdict);
        Assert.Equal(PerformanceVerdict.Fail, benchmark.Verdict);
        Assert.Equal(
            MeanDecisionQualityStatus.ConclusiveExceededBudget,
            benchmark.MeanDecisionQuality.Status);
    }

    [Fact]
    public void Unavailable_optional_metric_does_not_block_pass()
    {
        var result = Check(
            Measurement(100, null),
            Measurement(101, null),
            new PerformanceBudget(5, null));

        Assert.Equal(PerformanceVerdict.Pass, result.Verdict);
        Assert.Equal(PerformanceVerdict.Pass, Assert.Single(result.Benchmarks).Verdict);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Mean_budget_crossed_by_confidence_range_is_inconclusive()
    {
        var result = Check(
            Measurement(100, 64, 99, 101),
            Measurement(106, 64, 104, 108),
            new PerformanceBudget(5, null));

        var benchmark = Assert.Single(result.Benchmarks);
        Assert.True(benchmark.Result.MeanExceeded);
        Assert.Equal(PerformanceVerdict.Inconclusive, result.Verdict);
        Assert.Equal(
            MeanDecisionQualityStatus.Inconclusive,
            benchmark.MeanDecisionQuality.Status);
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("confidence range", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_statistics_is_inconclusive_when_mean_budget_is_configured()
    {
        var result = Check(
            new BenchmarkMeasurement("Sample.Work", 100, 64),
            new BenchmarkMeasurement("Sample.Work", 101, 64),
            new PerformanceBudget(5, null));

        Assert.Equal(PerformanceVerdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("statistics are unavailable", StringComparison.Ordinal));
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

    private static BenchmarkMeasurement Measurement(
        double mean,
        long? allocatedBytes,
        double? lower = null,
        double? upper = null) =>
        new(
            "Sample.Work",
            mean,
            allocatedBytes,
            new BenchmarkStatistics(
                15,
                mean,
                0.1,
                0.03,
                0,
                lower ?? Math.Max(0, mean - 0.1),
                upper ?? mean + 0.1,
                99.9));
}
