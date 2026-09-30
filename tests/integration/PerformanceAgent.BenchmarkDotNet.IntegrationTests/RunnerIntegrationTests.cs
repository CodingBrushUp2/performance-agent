using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;
using Xunit;

namespace PerformanceAgent.BenchmarkDotNet.IntegrationTests;

public sealed class RunnerIntegrationTests
{
    [Fact]
    public void Run_ExecutesRealBenchmarkAndReturnsNormalizedEvidence()
    {
        var runner = new BenchmarkDotNetRunner();

        var measurements = runner.Run<SampleBenchmark>();

        var measurement = Assert.Single(measurements);
        Assert.Equal(nameof(SampleBenchmark.Sum), measurement.Name);
        Assert.True(measurement.MeanNanoseconds >= 0);
        Assert.True(measurement.AllocatedBytesPerOperation >= 0);
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
