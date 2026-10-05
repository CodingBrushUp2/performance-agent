using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Quality;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class MeanDecisionQualityEvaluatorTests
{
    private readonly MeanDecisionQualityEvaluator _sut = new();

    [Fact]
    public void No_mean_budget_is_not_configured()
    {
        var result = _sut.Evaluate(Measurement(100, 99.9, 100.1), Measurement(104, 103.9, 104.1), null);

        Assert.Equal(MeanDecisionQualityStatus.NotConfigured, result.Status);
    }

    [Fact]
    public void Confidence_range_entirely_within_budget_is_conclusive_pass()
    {
        var result = _sut.Evaluate(
            Measurement(100, 99.9, 100.1),
            Measurement(104, 103.9, 104.1),
            5);

        Assert.Equal(MeanDecisionQualityStatus.ConclusiveWithinBudget, result.Status);
        Assert.False(result.IsInconclusive);
        Assert.False(result.IsExceeded);
        Assert.True(result.MaximumRegressionPercent <= 5);
        Assert.Equal(99.9, result.ConfidenceLevelPercent);
    }

    [Fact]
    public void Confidence_range_entirely_above_budget_is_conclusive_failure()
    {
        var result = _sut.Evaluate(
            Measurement(100, 99.9, 100.1),
            Measurement(106, 105.9, 106.1),
            5);

        Assert.Equal(MeanDecisionQualityStatus.ConclusiveExceededBudget, result.Status);
        Assert.True(result.IsExceeded);
        Assert.True(result.MinimumRegressionPercent > 5);
    }

    [Fact]
    public void Confidence_range_crossing_budget_is_inconclusive()
    {
        var result = _sut.Evaluate(
            Measurement(100, 99, 101),
            Measurement(105, 104, 106),
            5);

        Assert.Equal(MeanDecisionQualityStatus.Inconclusive, result.Status);
        Assert.True(result.IsInconclusive);
        Assert.True(result.MinimumRegressionPercent < 5);
        Assert.True(result.MaximumRegressionPercent > 5);
        Assert.Contains("crosses", result.Reason);
    }

    [Fact]
    public void Missing_statistics_is_inconclusive()
    {
        var result = _sut.Evaluate(
            new BenchmarkMeasurement("Sample.Work", 100, 64),
            Measurement(104, 103.9, 104.1),
            5);

        Assert.Equal(MeanDecisionQualityStatus.Inconclusive, result.Status);
        Assert.Contains("statistics are unavailable", result.Reason);
    }

    [Fact]
    public void Nonstandard_confidence_level_is_inconclusive()
    {
        var baseline = Measurement(100, 99.9, 100.1) with
        {
            Statistics = Measurement(100, 99.9, 100.1).Statistics! with
            {
                ConfidenceLevelPercent = 95
            }
        };

        var result = _sut.Evaluate(
            baseline,
            Measurement(104, 103.9, 104.1),
            5);

        Assert.Equal(MeanDecisionQualityStatus.Inconclusive, result.Status);
        Assert.Contains("99.9%", result.Reason);
    }

    [Fact]
    public void Missing_confidence_interval_is_inconclusive()
    {
        var result = _sut.Evaluate(
            MeasurementWithoutInterval(100),
            MeasurementWithoutInterval(104),
            5);

        Assert.Equal(MeanDecisionQualityStatus.Inconclusive, result.Status);
        Assert.Contains("confidence interval is unavailable", result.Reason);
    }

    private static BenchmarkMeasurement Measurement(double mean, double lower, double upper) =>
        new(
            "Sample.Work",
            mean,
            64,
            new BenchmarkStatistics(
                15,
                mean,
                0.1,
                0.03,
                0,
                lower,
                upper,
                99.9));

    private static BenchmarkMeasurement MeasurementWithoutInterval(double mean) =>
        new(
            "Sample.Work",
            mean,
            64,
            new BenchmarkStatistics(
                15,
                mean,
                0.1,
                0.03,
                0));
}
