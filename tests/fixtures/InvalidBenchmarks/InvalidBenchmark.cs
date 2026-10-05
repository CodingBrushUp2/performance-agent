using BenchmarkDotNet.Attributes;

public class InvalidBenchmark
{
    [Benchmark]
    public int Work(int value) => value;
}
