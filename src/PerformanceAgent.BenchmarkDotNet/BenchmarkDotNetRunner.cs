using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.BenchmarkDotNet;

public sealed class BenchmarkDotNetRunner
{
    private readonly BenchmarkDotNetMeasurementMapper _mapper = new();

    public IReadOnlyList<BenchmarkMeasurement> Run<TBenchmark>()
    {
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(Job.Dry.WithId("PerformanceAgent-Dry"));

        var summary = BenchmarkRunner.Run<TBenchmark>(config);

        return summary.Reports
            .Select(_mapper.Map)
            .ToArray();
    }
}
