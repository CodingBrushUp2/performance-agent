using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;
using PerformanceAgent.Core.Evidence;

var outputPath = GetOption(args, "--performance-agent-output");
var runner = new BenchmarkDotNetRunner();
var measurements = runner.RunDry<SampleBenchmark>();
var evidence = new BenchmarkEvidence("1.0", measurements);
var json = new JsonBenchmarkEvidenceWriter().Write(evidence);

if (outputPath is null)
{
    Console.WriteLine(json);
}
else
{
    var fullPath = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, json);
}

static string? GetOption(string[] arguments, string name)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[i + 1];
        }
    }

    return null;
}

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
