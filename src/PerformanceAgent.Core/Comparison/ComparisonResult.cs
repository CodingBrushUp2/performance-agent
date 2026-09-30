namespace PerformanceAgent.Core.Comparison;

public sealed record MetricChange(double Baseline, double Candidate, double PercentChange);

public sealed record ComparisonResult(
    string BenchmarkName,
    MetricChange Mean,
    MetricChange AllocatedBytes);
