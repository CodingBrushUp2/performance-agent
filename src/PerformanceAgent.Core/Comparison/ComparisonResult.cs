namespace PerformanceAgent.Core.Comparison;

public enum ComparisonStatus
{
    Comparable,
    NoBaseline
}

public sealed record MetricChange(
    double Baseline,
    double Candidate,
    double? PercentChange,
    ComparisonStatus Status);

public sealed record ComparisonResult(
    string BenchmarkName,
    MetricChange Mean,
    MetricChange AllocatedBytes);
