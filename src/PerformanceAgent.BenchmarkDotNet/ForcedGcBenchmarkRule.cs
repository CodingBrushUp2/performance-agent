using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Running;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class ForcedGcBenchmarkRule
{
    public const string RuleId = "PA1002";

    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => unchecked((ushort)opCode.Value));

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
            bool callsForcedGc;
            try
            {
                callsForcedGc = CallsForcedGarbageCollection(method);
            }
            catch (Exception exception) when (exception is ArgumentException
                                                or BadImageFormatException
                                                or InvalidOperationException
                                                or IndexOutOfRangeException)
            {
                // PA1002 is a best-effort warning. IL we cannot safely inspect must
                // never turn an otherwise valid benchmark into a validation failure.
                continue;
            }

            if (!callsForcedGc)
                continue;

            yield return new BenchmarkValidationDiagnostic(
                $"PerformanceAgent.{RuleId}",
                BenchmarkValidationSeverity.Warning,
                benchmarkType.FullName ?? benchmarkType.Name,
                method.Name,
                $"{RuleId}: GC.Collect() is called inside the measured benchmark method. Forced collection can distort timing and allocation evidence; move it outside the measured region unless garbage collection itself is the subject of the benchmark.");
        }
    }

    private static bool CallsForcedGarbageCollection(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes is null || bytes.Length == 0)
            return false;

        var position = 0;
        while (position < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref position);

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                EnsureAvailable(bytes, position, sizeof(int));
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
                    // An unresolvable call is unrelated to this narrow rule.
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
        EnsureAvailable(bytes, position, 1);
        var first = bytes[position++];
        var value = first == 0xFE
            ? ReadTwoByteOpCode(bytes, ref position)
            : first;

        if (!OpCodesByValue.TryGetValue(value, out var opCode))
            throw new InvalidOperationException($"Unknown IL opcode 0x{value:X4}.");

        return opCode;
    }

    private static ushort ReadTwoByteOpCode(byte[] bytes, ref int position)
    {
        EnsureAvailable(bytes, position, 1);
        return (ushort)(0xFE00 | bytes[position++]);
    }

    private static void SkipOperand(byte[] bytes, OperandType operandType, ref int position)
    {
        var size = operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField
                or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => ReadSwitchSize(bytes, position),
            _ => throw new InvalidOperationException($"Unsupported IL operand type '{operandType}'.")
        };

        EnsureAvailable(bytes, position, size);
        position += size;
    }

    private static int ReadSwitchSize(byte[] bytes, int position)
    {
        EnsureAvailable(bytes, position, sizeof(int));
        var caseCount = BitConverter.ToInt32(bytes, position);
        if (caseCount < 0)
            throw new InvalidOperationException("Invalid IL switch case count.");

        return checked(sizeof(int) + (caseCount * sizeof(int)));
    }

    private static void EnsureAvailable(byte[] bytes, int position, int length)
    {
        if (position < 0 || length < 0 || position > bytes.Length - length)
            throw new InvalidOperationException("Malformed IL while evaluating benchmark quality.");
    }
}
