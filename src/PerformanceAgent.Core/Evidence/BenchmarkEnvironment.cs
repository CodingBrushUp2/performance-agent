namespace PerformanceAgent.Core.Evidence;

public sealed record BenchmarkEnvironment(
    string Runtime,
    string OperatingSystem,
    string Architecture,
    int? LogicalProcessorCount = null,
    bool? ServerGarbageCollection = null);
