using System.Diagnostics;
using BenchmarkDotNet.Attributes;

public class SuspiciousBenchmark
{
    private int _value = 41;

    [Benchmark]
    public int ConstantWork() => 42;

    [Benchmark]
    public long ManualTiming()
    {
        var start = Stopwatch.GetTimestamp();
        var value = _value + 1;
        return Stopwatch.GetTimestamp() - start + value;
    }
}
