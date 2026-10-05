using BenchmarkDotNet.Running;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class ForcedGcRule
{
    public const string RuleId = "PA1003";

    public static IEnumerable<BenchmarkValidationDiagnostic> Analyze(
        Type benchmarkType,
        BenchmarkRunInfo runInfo)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);
        ArgumentNullException.ThrowIfNull(runInfo);

        foreach (var method in runInfo.BenchmarksCases
                     .Select(benchmark => benchmark.Descriptor.WorkloadMethod)
                     .Distinct())
        {
            if (!MethodCallScanner.Scan(method).Any(IsForcedGcCall))
                continue;

            yield return new BenchmarkValidationDiagnostic(
                $"PerformanceAgent.{RuleId}",
                BenchmarkValidationSeverity.Warning,
                benchmarkType.FullName ?? benchmarkType.Name,
                method.Name,
                $"{RuleId}: Benchmark body directly calls GC.Collect(). Forced collection can distort timing and allocation evidence; move it outside the measured region unless garbage collection itself is the intended subject.");
        }
    }

    private static bool IsForcedGcCall(System.Reflection.MethodBase method) =>
        method.DeclaringType == typeof(GC)
        && string.Equals(method.Name, nameof(GC.Collect), StringComparison.Ordinal);
}
