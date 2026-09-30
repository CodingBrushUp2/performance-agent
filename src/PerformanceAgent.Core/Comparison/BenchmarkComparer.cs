using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Comparison;

public sealed class BenchmarkComparer
{
    public ComparisonResult Compare(BenchmarkMeasurement baseline, BenchmarkMeasurement candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.Equals(baseline.Name, candidate.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("Baseline and candidate must represent the same benchmark.");
        }

        return new ComparisonResult(
            baseline.Name,
            CreateChange(baseline.MeanNanoseconds, candidate.MeanNanoseconds),
            CreateChange(baseline.AllocatedBytesPerOperation, candidate.AllocatedBytesPerOperation));
    }

    private static MetricChange CreateChange(double baseline, double candidate)
    {
        if (baseline < 0 || candidate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline), "Performance measurements cannot be negative.");
        }

        var percentChange = baseline == 0
            ? candidate == 0 ? 0 : double.PositiveInfinity
            : ((candidate - baseline) / baseline) * 100;

        return new MetricChange(baseline, candidate, percentChange);
    }
}
