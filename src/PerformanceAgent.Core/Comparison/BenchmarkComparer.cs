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

    private static MetricChange CreateChange(double? baseline, double? candidate)
    {
        if (baseline is null || candidate is null)
        {
            return new MetricChange(baseline, candidate, null, ComparisonStatus.Unavailable);
        }

        var baselineValue = baseline.Value;
        var candidateValue = candidate.Value;
        if (baselineValue < 0 || candidateValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline), "Performance measurements cannot be negative.");
        }

        if (baselineValue == 0)
        {
            return candidateValue == 0
                ? new MetricChange(0, 0, 0, ComparisonStatus.Comparable)
                : new MetricChange(0, candidateValue, null, ComparisonStatus.NoBaseline);
        }

        var percentChange = ((candidateValue - baselineValue) / baselineValue) * 100;
        return new MetricChange(baselineValue, candidateValue, percentChange, ComparisonStatus.Comparable);
    }
}
