namespace PerformanceAgent.Core.History;

public sealed record BenchmarkHistory(
    string SchemaVersion,
    IReadOnlyList<ArchivedBenchmarkRun> Runs,
    IReadOnlyList<BaselineEvent> BaselineEvents);
