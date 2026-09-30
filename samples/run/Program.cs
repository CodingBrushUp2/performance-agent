using System.Text.Json;
using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;

var runner = new BenchmarkDotNetRunner();
var measurements = runner.RunDry<SampleBenchmark>();

Console.WriteLine(JsonSerializer.Serialize(new
{
    schemaVersion = "1.0",
    measurements
}, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
}));

[MemoryDiagnoser]
public class SampleBenchmark
{
    private readonly int[] _values = Enumerable.Range(1, 100).ToArray();

    [Benchmark]
    public int Sum()
    {
        var sum = 0;
        foreach (var value in _values)
        {
            sum += value;
        }

        return sum;
    }
}
