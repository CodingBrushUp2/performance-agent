using BenchmarkDotNet.Running;

namespace PerformanceAgent.BenchmarkDotNet;

public static class BenchmarkCaseNameFormatter
{
    public static string Format(BenchmarkCase benchmarkCase)
    {
        ArgumentNullException.ThrowIfNull(benchmarkCase);

        var descriptor = benchmarkCase.Descriptor;
        var typeName = descriptor.Type.FullName ?? descriptor.Type.Name;
        var workloadName = $"{typeName}.{descriptor.WorkloadMethod.Name}";

        return benchmarkCase.HasParameters
            ? $"{workloadName}{benchmarkCase.Parameters.DisplayInfo}"
            : workloadName;
    }
}
