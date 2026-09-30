using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.BenchmarkDotNet;

public sealed class BenchmarkDotNetRunner
{
    private readonly BenchmarkDotNetMeasurementMapper _mapper = new();

    public IReadOnlyList<BenchmarkMeasurement> Run<TBenchmark>() =>
        Run(typeof(TBenchmark));

    public IReadOnlyList<BenchmarkMeasurement> Run(Type benchmarkType)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);
        return Map(BenchmarkRunner.Run(benchmarkType, DefaultConfig.Instance));
    }

    public IReadOnlyList<BenchmarkMeasurement> RunDry<TBenchmark>() =>
        RunDry(typeof(TBenchmark));

    public IReadOnlyList<BenchmarkMeasurement> RunDry(Type benchmarkType)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);

        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(Job.Dry.WithId("PerformanceAgent-Dry"));

        return Map(BenchmarkRunner.Run(benchmarkType, config));
    }

    public IReadOnlyList<Type> DiscoverBenchmarkTypes(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && type.GetMethods()
                .Any(method => method.GetCustomAttributes(typeof(global::BenchmarkDotNet.Attributes.BenchmarkAttribute), inherit: true).Length > 0))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<BenchmarkMeasurement> Map(global::BenchmarkDotNet.Reports.Summary summary) =>
        summary.Reports.Select(_mapper.Map).ToArray();
}
