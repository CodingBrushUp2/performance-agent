using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;
using Xunit;

namespace PerformanceAgent.BenchmarkDotNet.IntegrationTests;

public sealed class RunnerIntegrationTests
{
    [Fact]
    public void DiscoverBenchmarkTypes_FindsAttributedBenchmarks()
    {
        var runner = new BenchmarkDotNetRunner();

        var types = runner.DiscoverBenchmarkTypes(typeof(RunnerIntegrationTests).Assembly);

        Assert.Contains(typeof(SampleBenchmark), types);
    }

    [Fact]
    public void RunDry_ExecutesDiscoveredBenchmarkAndReturnsNormalizedEvidence()
    {
        var runner = new BenchmarkDotNetRunner();
        var benchmarkType = Assert.Single(
            runner.DiscoverBenchmarkTypes(typeof(RunnerIntegrationTests).Assembly),
            type => type == typeof(SampleBenchmark));

        var measurements = runner.RunDry(benchmarkType);

        var measurement = Assert.Single(measurements);
        Assert.Equal($"{typeof(SampleBenchmark).FullName}.{nameof(SampleBenchmark.Sum)}", measurement.Name);
        Assert.True(measurement.MeanNanoseconds >= 0);
        Assert.True(measurement.AllocatedBytesPerOperation is null or >= 0);
    }

    [Fact]
    public void RunDry_DistinguishesParameterizedBenchmarkCases()
    {
        var runner = new BenchmarkDotNetRunner();

        var measurements = runner.RunDry(typeof(ParameterizedBenchmark));

        Assert.Equal(2, measurements.Count);
        Assert.Equal(2, measurements.Select(measurement => measurement.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            measurements,
            measurement => Assert.StartsWith(
                $"{typeof(ParameterizedBenchmark).FullName}.{nameof(ParameterizedBenchmark.Work)}[Size=",
                measurement.Name,
                StringComparison.Ordinal));
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

    public class ParameterizedBenchmark
    {
        [Params(10, 100)]
        public int Size { get; set; }

        [Benchmark]
        public int Work() => Enumerable.Range(0, Size).Sum();
    }
}
