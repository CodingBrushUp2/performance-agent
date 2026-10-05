using BenchmarkDotNet.Attributes;

public class ForcedGcBenchmark
{
    private int _value = 41;

    [Benchmark]
    public int Work()
    {
        GC.Collect();
        return _value + 1;
    }
}
