using BenchmarkDotNet.Attributes;

public class SampleBenchmark
{
    private readonly int[] _values = Enumerable.Range(1, 1000).ToArray();

    [Benchmark]
    public int Sum() => _values.Sum();
}
