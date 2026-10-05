using System.Text.Json;
using PerformanceAgent.Core.Calibration;

namespace PerformanceAgent.Cli;

internal sealed record CalibrationJsonMetric(
    string BenchmarkName,
    double MedianMeanNanoseconds,
    double MeanSpreadPercent,
    long? MedianAllocatedBytesPerOperation,
    double? AllocationSpreadPercent);

internal sealed record CalibrationJsonDocument(
    string SchemaVersion,
    string Status,
    int RunCount,
    IReadOnlyList<string> RunIds,
    double MaxAllowedSpreadPercent,
    IReadOnlyList<CalibrationJsonMetric> Metrics,
    IReadOnlyList<string> Reasons);

internal sealed class CalibrationJsonWriter
{
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Write(
        CalibrationResult calibration,
        IReadOnlyList<string> runIds)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(runIds);

        if (runIds.Count < 2)
            throw new ArgumentException("Calibration JSON requires at least two run IDs.", nameof(runIds));

        var document = new CalibrationJsonDocument(
            SchemaVersion,
            calibration.IsStable ? "stable" : "unstable",
            runIds.Count,
            runIds,
            calibration.MaxAllowedSpreadPercent,
            calibration.Metrics
                .Select(metric => new CalibrationJsonMetric(
                    metric.BenchmarkName,
                    metric.MedianMeanNanoseconds,
                    metric.MeanSpreadPercent,
                    metric.MedianAllocatedBytesPerOperation,
                    metric.AllocationSpreadPercent))
                .ToArray(),
            calibration.InstabilityReasons);

        return JsonSerializer.Serialize(document, Options);
    }
}
