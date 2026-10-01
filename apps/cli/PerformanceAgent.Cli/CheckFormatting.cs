using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Comparison;

namespace PerformanceAgent.Cli;

// Shared by `check` and `analyze` (CLI and local UI) so measured results are presented identically.
internal static class CheckFormatting
{
    public static string FormatBudget(double? threshold, bool exceeded) =>
        threshold is null
            ? " (budget not configured)"
            : $" (budget +{threshold:0.##}%) {(exceeded ? "FAIL" : "PASS")}";

    public static string FormatChange(MetricChange change) =>
        change.Status == ComparisonStatus.Comparable && change.PercentChange is not null
            ? $"{change.Baseline:0.##} -> {change.Candidate:0.##} ({change.PercentChange:+0.##;-0.##;0}%)"
            : $"{change.Baseline?.ToString() ?? "n/a"} -> {change.Candidate?.ToString() ?? "n/a"} ({change.Status})";

    /// <summary>
    /// Mean and allocation lines for one benchmark, always derived from the deterministic comparison and budget.
    /// Analysis output uses this for every measured value it displays, including benchmarks cited by the model.
    /// </summary>
    public static (string Mean, string Allocation) FormatMeasured(BudgetCheckResult result, PerformanceBudget budget) =>
        (FormatChange(result.Comparison.Mean) + FormatBudget(budget.MaxMeanRegressionPercent, result.MeanExceeded),
         FormatChange(result.Comparison.AllocatedBytes) + FormatBudget(budget.MaxAllocationRegressionPercent, result.AllocationExceeded));
}
