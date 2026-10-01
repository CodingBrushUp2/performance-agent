using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using PerformanceAgent.BenchmarkDotNet;
using PerformanceAgent.Core.Evidence;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PerformanceAgent.BenchmarkHost <benchmark-assembly> <evidence-output>");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Benchmark assembly not found: {assemblyPath}");
    return 2;
}

var runner = new BenchmarkDotNetRunner();
BenchmarkDiscoveryResult discovery;
try
{
    var resolver = new AssemblyDependencyResolver(assemblyPath);
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        var path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    };

    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    discovery = runner.DiscoverBenchmarks(assembly);
}
catch (Exception exception) when (exception is IOException or BadImageFormatException or TypeLoadException or InvalidOperationException)
{
    Console.Error.WriteLine($"Cannot load benchmark assembly '{assemblyPath}': {exception.Message} Restore dependencies and rebuild the benchmark project for the installed runtime.");
    return 2;
}

foreach (var diagnostic in discovery.Diagnostics)
    Console.Error.WriteLine($"Benchmark discovery warning: {diagnostic}");
var benchmarkTypes = discovery.BenchmarkTypes;

if (benchmarkTypes.Count == 0)
{
    Console.Error.WriteLine(discovery.Diagnostics.Count == 0
        ? "No BenchmarkDotNet [Benchmark] methods were discovered."
        : "No loadable BenchmarkDotNet benchmark types remain. Resolve the discovery warnings before retrying.");
    return 2;
}

PerformanceAgent.Core.Measurements.BenchmarkMeasurement[] measurements;
try
{
    measurements = benchmarkTypes.SelectMany(runner.Run).ToArray();
}
catch (Exception exception) when (exception is ReflectionTypeLoadException or TypeLoadException or FileLoadException or FileNotFoundException or BadImageFormatException)
{
    var details = exception is ReflectionTypeLoadException load
        ? string.Join(Environment.NewLine, load.LoaderExceptions.OfType<Exception>().Select(error => error.Message))
        : exception.Message;
    Console.Error.WriteLine($"Benchmark execution could not load required types: {details} Restore dependencies and rebuild the benchmark project.");
    return 2;
}
var environment = new BenchmarkEnvironment(
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString());
var evidence = new BenchmarkEvidence("1.0", measurements, environment);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, new JsonBenchmarkEvidenceWriter().Write(evidence));
return 0;
