using PerformanceAgent.Core.Budgets;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class JsonPerformanceBudgetReaderTests
{
    [Fact]
    public void Read_ParsesConfiguredThresholds()
    {
        var budget = new JsonPerformanceBudgetReader().Read(
            """{"maxMeanRegressionPercent":5,"maxAllocationRegressionPercent":10}""");

        Assert.Equal(5, budget.MaxMeanRegressionPercent);
        Assert.Equal(10, budget.MaxAllocationRegressionPercent);
    }

    [Fact]
    public void Read_AllowsOneMetricToBeUnconfigured()
    {
        var budget = new JsonPerformanceBudgetReader().Read(
            """{"maxMeanRegressionPercent":5}""");

        Assert.Equal(5, budget.MaxMeanRegressionPercent);
        Assert.Null(budget.MaxAllocationRegressionPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{")]
    public void Read_RejectsInvalidBudget(string json)
    {
        Assert.ThrowsAny<ArgumentException>(() => new JsonPerformanceBudgetReader().Read(json));
    }
}
