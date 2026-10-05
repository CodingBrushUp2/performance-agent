using System.Diagnostics;
using BenchmarkDotNet.Running;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class ManualTimingRule
{
    public const string RuleId = "PA1002";

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
            var timingCalls = MethodCallScanner.Scan(method)
                .Where(IsStopwatchTimingCall)
                .Select(call => call.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (timingCalls.Length == 0)
                continue;

            yield return new BenchmarkValidationDiagnostic(
                $"PerformanceAgent.{RuleId}",
                BenchmarkValidationSeverity.Warning,
                benchmarkType.FullName ?? benchmarkType.Name,
                method.Name,
                $"{RuleId}: Benchmark body directly performs manual Stopwatch timing ({string.Join(", ", timingCalls)}). BenchmarkDotNet already measures execution time; remove nested timing unless Stopwatch itself is the intended subject.");
        }
    }

    private static bool IsStopwatchTimingCall(System.Reflection.MethodBase method) =>
        method.DeclaringType == typeof(Stopwatch)
        && method.Name is nameof(Stopwatch.Start)
            or nameof(Stopwatch.Restart)
            or nameof(Stopwatch.StartNew)
            or nameof(Stopwatch.GetTimestamp)
            or nameof(Stopwatch.GetElapsedTime);
}
