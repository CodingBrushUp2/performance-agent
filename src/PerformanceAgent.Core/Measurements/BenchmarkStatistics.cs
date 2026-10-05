namespace PerformanceAgent.Core.Measurements;

public sealed record BenchmarkStatistics(
    int SampleCount,
    double MedianNanoseconds,
    double? StandardDeviationNanoseconds,
    double? StandardErrorNanoseconds,
    int OutlierCount,
    double? ConfidenceIntervalLowerNanoseconds = null,
    double? ConfidenceIntervalUpperNanoseconds = null,
    double? ConfidenceLevelPercent = null);
