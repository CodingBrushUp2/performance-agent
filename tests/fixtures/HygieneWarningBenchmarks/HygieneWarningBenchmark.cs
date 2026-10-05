using BenchmarkDotNet.Attributes;

public class HygieneWarningBenchmark
{
    [Benchmark]
    public int Work()
    {
        GC.Collect();
        return 42;
    }
}
