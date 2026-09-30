using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Evidence;

public sealed record BenchmarkEvidence(
    string SchemaVersion,
    IReadOnlyList<BenchmarkMeasurement> Measurements);
