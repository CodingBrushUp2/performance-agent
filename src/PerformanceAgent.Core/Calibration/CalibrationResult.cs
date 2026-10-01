namespace PerformanceAgent.Core.Calibration;

public sealed record CalibrationMetric(
    string BenchmarkName,
    double MedianMeanNanoseconds,
    double MeanSpreadPercent,
    long? MedianAllocatedBytesPerOperation,
    double? AllocationSpreadPercent);

public sealed record CalibrationResult(
    bool IsStable,
    double MaxAllowedSpreadPercent,
    IReadOnlyList<CalibrationMetric> Metrics,
    IReadOnlyList<string> InstabilityReasons);
