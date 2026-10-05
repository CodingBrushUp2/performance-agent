using BenchmarkDotNet.Attributes;

public class InvalidBenchmark
{
    [Benchmark]
    private int HiddenWork() => 42;
}
