using BenchmarkDotNet.Attributes;

public class BrokenBenchmark
{
    [Benchmark]
    public int Work() => MissingType.Value;
}
