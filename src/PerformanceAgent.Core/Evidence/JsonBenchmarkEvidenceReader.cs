using System.Text.Json;

namespace PerformanceAgent.Core.Evidence;

public sealed class JsonBenchmarkEvidenceReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BenchmarkEvidence Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Benchmark evidence JSON cannot be empty.", nameof(json));

        BenchmarkEvidence evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<BenchmarkEvidence>(json, Options)
                ?? throw new InvalidOperationException("Benchmark evidence JSON did not contain an evidence document.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Benchmark evidence is not valid JSON.", exception);
        }

        Validate(evidence);
        return evidence;
    }

    private static void Validate(BenchmarkEvidence evidence)
    {
        if (!string.Equals(evidence.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported benchmark evidence schema version: {evidence.SchemaVersion ?? "<missing>"}.");

        if (evidence.Measurements is null || evidence.Measurements.Count == 0)
            throw new InvalidOperationException("Benchmark evidence must contain at least one measurement.");

        if (evidence.Environment is not null)
        {
            if (string.IsNullOrWhiteSpace(evidence.Environment.Runtime))
                throw new InvalidOperationException("Benchmark environment runtime cannot be empty.");
            if (string.IsNullOrWhiteSpace(evidence.Environment.OperatingSystem))
                throw new InvalidOperationException("Benchmark environment operating system cannot be empty.");
            if (string.IsNullOrWhiteSpace(evidence.Environment.Architecture))
                throw new InvalidOperationException("Benchmark environment architecture cannot be empty.");
        }

        var duplicateName = evidence.Measurements
            .GroupBy(measurement => measurement?.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)?.Key;
        if (duplicateName is not null)
            throw new InvalidOperationException($"Benchmark evidence contains duplicate measurement identity '{duplicateName}'.");

        foreach (var measurement in evidence.Measurements)
        {
            if (measurement is null || string.IsNullOrWhiteSpace(measurement.Name))
                throw new InvalidOperationException("Benchmark evidence contains a measurement without a name.");
            if (!double.IsFinite(measurement.MeanNanoseconds) || measurement.MeanNanoseconds < 0)
                throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid mean duration.");
            if (measurement.AllocatedBytesPerOperation is < 0)
                throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid allocation measurement.");

            if (measurement.Statistics is { } statistics)
            {
                if (statistics.SampleCount <= 0)
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid statistics sample count.");
                if (!double.IsFinite(statistics.MedianNanoseconds) || statistics.MedianNanoseconds < 0)
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid statistics median.");
                if (statistics.StandardDeviationNanoseconds is { } standardDeviation
                    && (!double.IsFinite(standardDeviation) || standardDeviation < 0))
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid statistics standard deviation.");
                if (statistics.StandardErrorNanoseconds is { } standardError
                    && (!double.IsFinite(standardError) || standardError < 0))
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid statistics standard error.");
                if (statistics.OutlierCount < 0 || statistics.OutlierCount > statistics.SampleCount)
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid statistics outlier count.");

                var lower = statistics.ConfidenceIntervalLowerNanoseconds;
                var upper = statistics.ConfidenceIntervalUpperNanoseconds;
                if ((lower is null) != (upper is null))
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' must provide both confidence interval bounds or neither.");
                if (lower is { } lowerValue
                    && (!double.IsFinite(lowerValue) || lowerValue < 0))
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid confidence interval lower bound.");
                if (upper is { } upperValue
                    && (!double.IsFinite(upperValue) || upperValue < 0))
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid confidence interval upper bound.");
                if (lower is { } validLower && upper is { } validUpper && validLower > validUpper)
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' has an invalid confidence interval range.");
                if (lower is { } intervalLower
                    && upper is { } intervalUpper
                    && (measurement.MeanNanoseconds < intervalLower || measurement.MeanNanoseconds > intervalUpper))
                    throw new InvalidOperationException($"Benchmark '{measurement.Name}' mean must fall inside its confidence interval.");
            }
        }
    }
}
