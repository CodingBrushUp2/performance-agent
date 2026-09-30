using PerformanceAgent.Core.Evidence;

namespace PerformanceAgent.Core.History;

public sealed record ArchivedBenchmarkRun(
    string RunId,
    DateTimeOffset Timestamp,
    string? CommitSha,
    BenchmarkEvidence Evidence);
