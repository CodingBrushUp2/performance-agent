using System.Reflection;
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

    public IReadOnlyList<Type> DiscoverBenchmarkTypes(Assembly assembly)
    {
        var result = DiscoverBenchmarks(assembly);
        // Existing callers cannot consume diagnostics, so never return a silent partial result.
        if (result.Diagnostics.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return result.BenchmarkTypes;
    }

    public BenchmarkDiscoveryResult DiscoverBenchmarks(Assembly assembly) =>
        DiscoverBenchmarks(assembly, includeNonPublicBenchmarkMethods: false);

    public BenchmarkDiscoveryResult DiscoverBenchmarksForValidation(Assembly assembly) =>
        DiscoverBenchmarks(assembly, includeNonPublicBenchmarkMethods: true);

    private BenchmarkDiscoveryResult DiscoverBenchmarks(
        Assembly assembly,
        bool includeNonPublicBenchmarkMethods)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var diagnostics = new List<string>();
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>().ToArray();
            AddDiagnostics(exception, "assembly type loading", diagnostics);
        }
        catch (Exception exception) when (IsLoaderFailure(exception))
        {
            types = [];
            AddDiagnostics(exception, "assembly type loading", diagnostics);
        }

        var benchmarks = new List<Type>();
        foreach (var type in types)
        {
            try
            {
                var methods = includeNonPublicBenchmarkMethods
                    ? type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : type.GetMethods();

                if (!type.IsAbstract && methods.Any(method =>
                        method.IsDefined(typeof(global::BenchmarkDotNet.Attributes.BenchmarkAttribute), inherit: true)))
                    benchmarks.Add(type);
            }
            catch (Exception exception) when (IsLoaderFailure(exception))
            {
                AddDiagnostics(exception, $"type '{type.FullName}'", diagnostics);
            }
        }

        return new BenchmarkDiscoveryResult(
            benchmarks.Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray(),
            diagnostics.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static bool IsLoaderFailure(Exception exception) =>
        exception is ReflectionTypeLoadException or TypeLoadException or FileNotFoundException or FileLoadException or BadImageFormatException;

    private static void AddDiagnostics(Exception exception, string scope, List<string> diagnostics)
    {
        var failures = exception is ReflectionTypeLoadException reflection
            ? reflection.LoaderExceptions.OfType<Exception>().ToArray()
            : [exception];
        if (failures.Length == 0)
            failures = [exception];
        foreach (var failure in failures)
            diagnostics.Add($"Discovery incomplete during {scope}: {failure.GetType().Name}: {failure.Message} Restore dependencies and rebuild the benchmark project for the installed runtime.");
    }

    private IReadOnlyList<BenchmarkMeasurement> Map(global::BenchmarkDotNet.Reports.Summary summary)
    {
        // A report without statistics means BenchmarkDotNet could not build or run the case.
        // Fail with an actionable message instead of a mapper exception, and never emit partial evidence.
        var failed = summary.Reports
            .Where(report => report.ResultStatistics is null)
            .Select(report => report.BenchmarkCase.DisplayInfo)
            .ToArray();
        if (failed.Length != 0 || summary.Reports.Length == 0)
            throw new BenchmarkExecutionException(failed, summary.LogFilePath);

        return summary.Reports.Select(_mapper.Map).ToArray();
    }
}
