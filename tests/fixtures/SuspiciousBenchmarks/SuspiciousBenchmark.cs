using BenchmarkDotNet.Attributes;

public class SuspiciousBenchmark
{
    [Benchmark]
    public int Work() => 42;
}
