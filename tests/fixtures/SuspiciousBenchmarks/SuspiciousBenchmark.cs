using BenchmarkDotNet.Attributes;

public class SuspiciousBenchmark
{
    [Benchmark]
    public int ConstantWork() => 42;
}
