using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class PerformanceBudgetCheckerTests
{
    [Fact]
    public void Check_PassesWhenRegressionsStayWithinBudget()
    {
        var baseline = new BenchmarkMeasurement("A", 100, 1000);
        var candidate = new BenchmarkMeasurement("A", 104, 1050);

        var result = new PerformanceBudgetChecker().Check(
            baseline,
            candidate,
            new PerformanceBudget(5, 10));

        Assert.True(result.Passed);
        Assert.False(result.MeanExceeded);
        Assert.False(result.AllocationExceeded);
    }

    [Fact]
    public void Check_FailsWhenMeanRegressionExceedsBudget()
    {
        var baseline = new BenchmarkMeasurement("A", 100, 1000);
        var candidate = new BenchmarkMeasurement("A", 106, 1000);

        var result = new PerformanceBudgetChecker().Check(
            baseline,
            candidate,
            new PerformanceBudget(5, 10));

        Assert.False(result.Passed);
        Assert.True(result.MeanExceeded);
        Assert.False(result.AllocationExceeded);
    }

    [Fact]
    public void Check_DoesNotTreatUnavailableAllocationAsRegression()
    {
        var baseline = new BenchmarkMeasurement("A", 100, null);
        var candidate = new BenchmarkMeasurement("A", 100, 1000);

        var result = new PerformanceBudgetChecker().Check(
            baseline,
            candidate,
            new PerformanceBudget(null, 0));

        Assert.True(result.Passed);
        Assert.False(result.AllocationExceeded);
    }

    [Fact]
    public void Check_RejectsInvalidBudget()
    {
        var measurement = new BenchmarkMeasurement("A", 100, 1000);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PerformanceBudgetChecker().Check(
                measurement,
                measurement,
                new PerformanceBudget(-1, null)));
    }
}
