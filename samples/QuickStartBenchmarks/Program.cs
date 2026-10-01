using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<StringBuildingBenchmarks>();

[MemoryDiagnoser]
public class StringBuildingBenchmarks
{
    private readonly string[] _parts = Enumerable.Range(0, 100)
        .Select(index => $"item-{index}")
        .ToArray();

    [Benchmark(Baseline = true)]
    public string Concatenate()
    {
        var result = string.Empty;
        foreach (var part in _parts)
            result += part;
        return result;
    }

    [Benchmark]
    public string Join() => string.Join(',', _parts);
}
