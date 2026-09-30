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
        Assert.Equal(nameof(SampleBenchmark.Sum), measurement.Name);
        Assert.True(measurement.MeanNanoseconds >= 0);
        Assert.True(measurement.AllocatedBytesPerOperation is null or >= 0);
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
}
