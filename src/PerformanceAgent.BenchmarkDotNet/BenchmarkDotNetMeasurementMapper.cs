using BenchmarkDotNet.Reports;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.BenchmarkDotNet;

public sealed class BenchmarkDotNetMeasurementMapper
{
    public BenchmarkMeasurement Map(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var statistics = report.ResultStatistics
            ?? throw new InvalidOperationException("Benchmark report does not contain timing statistics.");

        var descriptor = report.BenchmarkCase.Descriptor;
        var typeName = descriptor.Type.FullName ?? descriptor.Type.Name;
        var workloadName = $"{typeName}.{descriptor.WorkloadMethod.Name}";
        var name = report.BenchmarkCase.HasParameters
            ? $"{workloadName}{report.BenchmarkCase.Parameters.DisplayInfo}"
            : workloadName;
        var allocatedBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
        var confidenceInterval = statistics.ConfidenceInterval;

        var normalizedStatistics = new BenchmarkStatistics(
            statistics.N,
            statistics.Median,
            FiniteOrNull(statistics.StandardDeviation),
            FiniteOrNull(statistics.StandardError),
            statistics.AllOutliers.Length,
            FiniteOrNull(confidenceInterval.Lower),
            FiniteOrNull(confidenceInterval.Upper));

        return new BenchmarkMeasurement(
            name,
            statistics.Mean,
            allocatedBytes,
            normalizedStatistics);
    }

    private static double? FiniteOrNull(double value) =>
        double.IsFinite(value) ? value : null;
}
