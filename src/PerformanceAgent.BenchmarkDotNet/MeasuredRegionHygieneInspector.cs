using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Running;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class MeasuredRegionHygieneInspector
{
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => unchecked((ushort)opCode.Value));

    public static IEnumerable<BenchmarkValidationDiagnostic> Inspect(
        Type benchmarkType,
        BenchmarkRunInfo runInfo)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);
        ArgumentNullException.ThrowIfNull(runInfo);

        foreach (var method in runInfo.BenchmarksCases
                     .Select(benchmark => benchmark.Descriptor.WorkloadMethod)
                     .Distinct())
        {
            if (CallsForcedGarbageCollection(method))
            {
                yield return new BenchmarkValidationDiagnostic(
                    "PerformanceAgent.MeasuredRegionHygiene",
                    BenchmarkValidationSeverity.Warning,
                    benchmarkType.FullName ?? benchmarkType.Name,
                    method.Name,
                    "GC.Collect() is called inside the measured benchmark method. Forced collection can distort timing and allocation evidence; move it outside the measured region unless garbage collection itself is the subject of the benchmark.");
            }
        }
    }

    private static bool CallsForcedGarbageCollection(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (bytes is null || bytes.Length == 0)
            return false;

        var position = 0;
        while (position < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref position);

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(bytes, position);
                position += sizeof(int);

                MethodBase? calledMethod = null;
                try
                {
                    calledMethod = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.GetGenericArguments(),
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (ArgumentException)
                {
                    // Unresolvable metadata should not turn a best-effort hygiene warning
                    // into a validation failure.
                }

                if (calledMethod?.DeclaringType == typeof(GC)
                    && string.Equals(calledMethod.Name, nameof(GC.Collect), StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            SkipOperand(bytes, opCode.OperandType, ref position);
        }

        return false;
    }

    private static OpCode ReadOpCode(byte[] bytes, ref int position)
    {
        var first = bytes[position++];
        var value = first == 0xFE
            ? (ushort)(0xFE00 | bytes[position++])
            : first;

        if (!OpCodesByValue.TryGetValue(value, out var opCode))
            throw new InvalidOperationException($"Unknown IL opcode 0x{value:X4}.");

        return opCode;
    }

    private static void SkipOperand(byte[] bytes, OperandType operandType, ref int position)
    {
        position += operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI => 4,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => ReadSwitchSize(bytes, position),
            _ => throw new InvalidOperationException($"Unsupported IL operand type '{operandType}'.")
        };
    }

    private static int ReadSwitchSize(byte[] bytes, int position)
    {
        var caseCount = BitConverter.ToInt32(bytes, position);
        return sizeof(int) + (caseCount * sizeof(int));
    }
}
