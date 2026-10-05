namespace PerformanceAgent.Core.Quality;

public enum MeanDecisionQualityStatus
{
    NotConfigured,
    ConclusiveWithinBudget,
    ConclusiveExceededBudget,
    Inconclusive
}

public sealed record MeanDecisionQuality(
    MeanDecisionQualityStatus Status,
    double? MinimumRegressionPercent,
    double? MaximumRegressionPercent,
    double? ConfidenceLevelPercent,
    string? Reason)
{
    public bool IsInconclusive => Status == MeanDecisionQualityStatus.Inconclusive;
    public bool IsExceeded => Status == MeanDecisionQualityStatus.ConclusiveExceededBudget;
}
