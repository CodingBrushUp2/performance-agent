namespace PerformanceAgent.Core.Budgets;

public sealed record PerformanceBudget(
    double? MaxMeanRegressionPercent = null,
    double? MaxAllocationRegressionPercent = null);
