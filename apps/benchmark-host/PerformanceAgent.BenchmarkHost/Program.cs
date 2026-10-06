using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using PerformanceAgent.BenchmarkDotNet;
using PerformanceAgent.Core.Evidence;

if (args.Length == 2 && string.Equals(args[0], "--validate", StringComparison.Ordinal))
{
    return Validate(Path.GetFullPath(args[1]));
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PerformanceAgent.BenchmarkHost <benchmark-assembly> <evidence-output>");
    Console.Error.WriteLine("       PerformanceAgent.BenchmarkHost --validate <benchmark-assembly>");
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
    var assembly = LoadBenchmarkAssembly(assemblyPath);
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
catch (BenchmarkExecutionException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
var environment = new BenchmarkEnvironment(
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString(),
    Environment.ProcessorCount,
    GCSettings.IsServerGC);
var evidence = new BenchmarkEvidence("1.0", measurements, environment);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, new JsonBenchmarkEvidenceWriter().Write(evidence));
return 0;

static Assembly LoadBenchmarkAssembly(string assemblyPath)
{
    if (!File.Exists(assemblyPath))
        throw new FileNotFoundException($"Benchmark assembly not found: {assemblyPath}", assemblyPath);

    var resolver = new AssemblyDependencyResolver(assemblyPath);
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        var path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    };

    return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
}

static int Validate(string assemblyPath)
{
    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"Benchmark assembly not found: {assemblyPath}");
        return 2;
    }

    BenchmarkDiscoveryResult discovery;
    try
    {
        var assembly = LoadBenchmarkAssembly(assemblyPath);
        discovery = new BenchmarkDotNetRunner().DiscoverBenchmarksForValidation(assembly);
    }
    catch (Exception exception) when (exception is IOException or BadImageFormatException or TypeLoadException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Cannot load benchmark assembly '{assemblyPath}': {exception.Message} Restore dependencies and rebuild the benchmark project for the installed runtime.");
        return 2;
    }

    if (discovery.Diagnostics.Count != 0)
    {
        foreach (var diagnostic in discovery.Diagnostics)
            Console.Error.WriteLine($"Benchmark discovery warning: {diagnostic}");
        Console.Error.WriteLine("Benchmark validation is incomplete because discovery reported load failures.");
        return 2;
    }

    if (discovery.BenchmarkTypes.Count == 0)
    {
        Console.Error.WriteLine("No BenchmarkDotNet [Benchmark] methods were discovered.");
        return 2;
    }

    var validator = new BenchmarkDotNetValidator();
    var results = new List<BenchmarkValidationResult>(discovery.BenchmarkTypes.Count);
    try
    {
        foreach (var benchmarkType in discovery.BenchmarkTypes)
            results.Add(validator.Validate(benchmarkType));
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Benchmark validation failed: {exception.Message}");
        return 2;
    }

    var diagnostics = results.SelectMany(x => x.Diagnostics).ToArray();
    var document = new BenchmarkValidationDocument(
        "1.0",
        results.All(x => x.IsValid),
        discovery.BenchmarkTypes.Count,
        diagnostics);

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    Console.WriteLine(JsonSerializer.Serialize(document, options));
    return document.Valid ? 0 : 1;
}

internal sealed record BenchmarkValidationDocument(
    string SchemaVersion,
    bool Valid,
    int BenchmarkTypeCount,
    IReadOnlyList<BenchmarkValidationDiagnostic> Diagnostics);
