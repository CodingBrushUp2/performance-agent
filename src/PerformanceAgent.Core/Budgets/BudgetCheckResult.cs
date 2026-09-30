using PerformanceAgent.Core.Comparison;

namespace PerformanceAgent.Core.Budgets;

public sealed record BudgetCheckResult(
    string BenchmarkName,
    ComparisonResult Comparison,
    bool MeanExceeded,
    bool AllocationExceeded)
{
    public bool Passed => !MeanExceeded && !AllocationExceeded;
}
