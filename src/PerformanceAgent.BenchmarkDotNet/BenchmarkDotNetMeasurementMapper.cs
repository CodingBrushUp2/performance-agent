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

        var name = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
        var allocatedBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);

        return new BenchmarkMeasurement(
            name,
            statistics.Mean,
            allocatedBytes ?? 0);
    }
}
