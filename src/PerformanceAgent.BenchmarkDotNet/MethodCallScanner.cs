using System.Reflection;
using System.Reflection.Emit;

namespace PerformanceAgent.BenchmarkDotNet;

internal static class MethodCallScanner
{
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .GroupBy(opCode => unchecked((ushort)opCode.Value))
            .ToDictionary(group => group.Key, group => group.First());

    public static IEnumerable<MethodBase> Scan(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null || il.Length == 0)
            yield break;

        var typeArguments = method.DeclaringType?.GetGenericArguments();
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var index = 0;

        while (index < il.Length)
        {
            if (!TryReadOpCode(il, ref index, out var opCode))
                yield break;

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                if (index + sizeof(int) > il.Length)
                    yield break;

                var token = BitConverter.ToInt32(il, index);
                MethodBase? calledMethod = null;
                try
                {
                    calledMethod = method.Module.ResolveMethod(token, typeArguments, methodArguments);
                }
                catch (ArgumentException)
                {
                    // Invalid or context-dependent metadata token. Skip the unresolved call.
                }

                if (calledMethod is not null)
                    yield return calledMethod;
            }

            var operandSize = GetOperandSize(opCode, il, index);
            if (operandSize < 0 || index + operandSize > il.Length)
                yield break;

            index += operandSize;
        }
    }

    private static bool TryReadOpCode(byte[] il, ref int index, out OpCode opCode)
    {
        var first = il[index++];
        var value = first == 0xFE
            ? index < il.Length
                ? (ushort)(0xFE00 | il[index++])
                : ushort.MaxValue
            : first;

        return OpCodesByValue.TryGetValue(value, out opCode);
    }

    private static int GetOperandSize(OpCode opCode, byte[] il, int operandStart) =>
        opCode.OperandType switch
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
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => ReadSwitchSize(il, operandStart),
            _ => -1
        };

    private static int ReadSwitchSize(byte[] il, int operandStart)
    {
        if (operandStart + sizeof(int) > il.Length)
            return -1;

        var count = BitConverter.ToInt32(il, operandStart);
        if (count < 0)
            return -1;

        try
        {
            return checked(sizeof(int) + count * sizeof(int));
        }
        catch (OverflowException)
        {
            return -1;
        }
    }
}
