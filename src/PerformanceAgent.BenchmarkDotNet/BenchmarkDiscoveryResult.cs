namespace PerformanceAgent.BenchmarkDotNet;

public sealed record BenchmarkDiscoveryResult(
    IReadOnlyList<Type> BenchmarkTypes,
    IReadOnlyList<string> Diagnostics);
