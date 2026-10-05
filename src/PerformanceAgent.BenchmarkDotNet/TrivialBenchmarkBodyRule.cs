using System.Reflection;
using BenchmarkDotNet.Running;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class TrivialBenchmarkBodyRule
{
    public const string RuleId = "PA1001";

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
            if (!LooksTrivial(method))
                continue;

            yield return new BenchmarkValidationDiagnostic(
                $"PerformanceAgent.{RuleId}",
                BenchmarkValidationSeverity.Warning,
                benchmarkType.FullName ?? benchmarkType.Name,
                method.Name,
                $"{RuleId}: Benchmark body appears to return a constant or perform no work. Verify that it exercises the intended code path and cannot be optimized into a meaningless measurement.");
        }
    }

    private static bool LooksTrivial(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null || il.Length == 0)
            return false;

        var start = 0;
        while (start < il.Length && il[start] == 0x00) // nop
            start++;

        var end = il.Length - 1;
        while (end >= start && il[end] == 0x00) // nop
            end--;

        if (end < start || il[end] != 0x2A) // ret
            return false;

        var instructionLength = end - start;
        if (instructionLength == 0)
            return true; // ret only

        var opcode = il[start];
        return opcode switch
        {
            0x14 => instructionLength == 1, // ldnull
            >= 0x15 and <= 0x1E => instructionLength == 1, // ldc.i4.m1 .. ldc.i4.8
            0x1F => instructionLength == 2, // ldc.i4.s
            0x20 => instructionLength == 5, // ldc.i4
            0x21 => instructionLength == 9, // ldc.i8
            0x22 => instructionLength == 5, // ldc.r4
            0x23 => instructionLength == 9, // ldc.r8
            0x72 => instructionLength == 5, // ldstr
            _ => false
        };
    }
}
