using System.Reflection;
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

var resolver = new AssemblyDependencyResolver(assemblyPath);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = resolver.ResolveAssemblyToPath(name);
    return path is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
};

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var runner = new BenchmarkDotNetRunner();
var benchmarkTypes = runner.DiscoverBenchmarkTypes(assembly);

if (benchmarkTypes.Count == 0)
{
    Console.Error.WriteLine("No BenchmarkDotNet [Benchmark] methods were discovered.");
    return 2;
}

var measurements = benchmarkTypes.SelectMany(runner.Run).ToArray();
var evidence = new BenchmarkEvidence("1.0", measurements);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, new JsonBenchmarkEvidenceWriter().Write(evidence));
return 0;
