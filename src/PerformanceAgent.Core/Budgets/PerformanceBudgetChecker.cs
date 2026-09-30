using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Budgets;

public sealed class PerformanceBudgetChecker
{
    private readonly BenchmarkComparer _comparer = new();

    public BudgetCheckResult Check(
        BenchmarkMeasurement baseline,
        BenchmarkMeasurement candidate,
        PerformanceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ValidateBudget(budget);

        var comparison = _comparer.Compare(baseline, candidate);
        var meanExceeded = IsExceeded(comparison.Mean, budget.MaxMeanRegressionPercent);
        var allocationExceeded = IsExceeded(comparison.AllocatedBytes, budget.MaxAllocationRegressionPercent);

        return new BudgetCheckResult(
            comparison.BenchmarkName,
            comparison,
            meanExceeded,
            allocationExceeded);
    }

    private static bool IsExceeded(MetricChange change, double? maximumRegressionPercent) =>
        maximumRegressionPercent is not null
        && change.Status == ComparisonStatus.Comparable
        && change.PercentChange is not null
        && change.PercentChange.Value > maximumRegressionPercent.Value;

    private static void ValidateBudget(PerformanceBudget budget)
    {
        ValidateThreshold(budget.MaxMeanRegressionPercent, nameof(budget.MaxMeanRegressionPercent));
        ValidateThreshold(budget.MaxAllocationRegressionPercent, nameof(budget.MaxAllocationRegressionPercent));

        if (budget.MaxMeanRegressionPercent is null && budget.MaxAllocationRegressionPercent is null)
            throw new ArgumentException("At least one performance budget threshold must be configured.", nameof(budget));
    }

    private static void ValidateThreshold(double? value, string name)
    {
        if (value is not null && (!double.IsFinite(value.Value) || value.Value < 0))
            throw new ArgumentOutOfRangeException(name, "Performance budget thresholds must be finite, non-negative percentages.");
    }
}
