using System.Reflection;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class PerformanceAgentBenchmarkLinter
{
    private const string Source = "PerformanceAgentValidityGuard";

    public static IReadOnlyList<BenchmarkValidationDiagnostic> Inspect(
        Type benchmarkType,
        IEnumerable<MethodInfo> benchmarkMethods)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);
        ArgumentNullException.ThrowIfNull(benchmarkMethods);

        return benchmarkMethods
            .Distinct()
            .Where(IsTrivialConstantOrNoOp)
            .Select(method => new BenchmarkValidationDiagnostic(
                Source,
                BenchmarkValidationSeverity.Warning,
                benchmarkType.FullName ?? benchmarkType.Name,
                method.Name,
                "Benchmark body is a trivial constant or no-op. It is likely measuring invocation overhead rather than meaningful target work."))
            .ToArray();
    }

    private static bool IsTrivialConstantOrNoOp(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null || il.Length == 0)
            return false;

        var index = 0;
        while (index < il.Length && il[index] == 0x00) // nop
            index++;

        if (index >= il.Length)
            return false;

        if (il[index] == 0x2A) // ret
            return index + 1 == il.Length;

        var operandLength = ConstantLoadOperandLength(il[index]);
        if (operandLength < 0)
            return false;

        var retIndex = index + 1 + operandLength;
        while (retIndex < il.Length && il[retIndex] == 0x00) // nop
            retIndex++;

        return retIndex < il.Length
            && il[retIndex] == 0x2A
            && retIndex + 1 == il.Length;
    }

    private static int ConstantLoadOperandLength(byte opcode) =>
        opcode switch
        {
            0x14 => 0, // ldnull
            >= 0x15 and <= 0x1E => 0, // ldc.i4.m1 ... ldc.i4.8
            0x1F => 1, // ldc.i4.s
            0x20 => 4, // ldc.i4
            0x21 => 8, // ldc.i8
            0x22 => 4, // ldc.r4
            0x23 => 8, // ldc.r8
            0x72 => 4, // ldstr metadata token
            _ => -1
        };
}
