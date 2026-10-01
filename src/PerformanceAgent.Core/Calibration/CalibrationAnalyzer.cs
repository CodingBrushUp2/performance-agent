using PerformanceAgent.Core.Evidence;

namespace PerformanceAgent.Core.Calibration;

public sealed class CalibrationAnalyzer
{
    public CalibrationResult Analyze(
        IReadOnlyList<BenchmarkEvidence> samples,
        double maxAllowedSpreadPercent)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 2)
            throw new ArgumentException("Calibration requires at least two benchmark samples.", nameof(samples));
        if (!double.IsFinite(maxAllowedSpreadPercent) || maxAllowedSpreadPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAllowedSpreadPercent), "Maximum allowed spread must be a non-negative finite percentage.");

        var first = samples[0];
        var identities = first.Measurements.Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
        foreach (var sample in samples.Skip(1))
        {
            var environment = new BenchmarkEnvironmentComparer().Compare(first.Environment, sample.Environment);
            if (!environment.IsComparable)
                throw new InvalidOperationException($"Calibration samples use non-comparable environments: {string.Join("; ", environment.Differences)}");

            var sampleIdentities = sample.Measurements.Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
            if (!identities.SequenceEqual(sampleIdentities, StringComparer.Ordinal))
                throw new InvalidOperationException("Calibration samples must contain the same benchmark identities.");
        }

        var metrics = new List<CalibrationMetric>();
        var reasons = new List<string>();
        foreach (var name in identities)
        {
            var measurements = samples
                .Select(sample => sample.Measurements.Single(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
                .ToArray();

            var means = measurements.Select(x => x.MeanNanoseconds).Order().ToArray();
            var meanMedian = Median(means);
            var meanSpread = SpreadPercent(means, meanMedian);

            long? allocationMedian = null;
            double? allocationSpread = null;
            var allocations = measurements.Select(x => x.AllocatedBytesPerOperation).ToArray();
            if (allocations.All(x => x.HasValue))
            {
                var values = allocations.Select(x => (double)x!.Value).Order().ToArray();
                allocationMedian = checked((long)Math.Round(Median(values), MidpointRounding.AwayFromZero));
                allocationSpread = SpreadPercent(values, allocationMedian.Value);
            }

            if (meanSpread > maxAllowedSpreadPercent)
                reasons.Add($"{name}: mean spread {meanSpread:0.##}% exceeds {maxAllowedSpreadPercent:0.##}%.");
            if (allocationSpread is not null && allocationSpread > maxAllowedSpreadPercent)
                reasons.Add($"{name}: allocation spread {allocationSpread:0.##}% exceeds {maxAllowedSpreadPercent:0.##}%.");

            metrics.Add(new CalibrationMetric(name, meanMedian, meanSpread, allocationMedian, allocationSpread));
        }

        return new CalibrationResult(reasons.Count == 0, maxAllowedSpreadPercent, metrics, reasons);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }

    private static double SpreadPercent(IReadOnlyList<double> values, double median)
    {
        var spread = values[^1] - values[0];
        if (median == 0)
            return spread == 0 ? 0 : double.PositiveInfinity;
        return spread / median * 100d;
    }
}
