namespace PerformanceAgent.Core.Analysis;

/// <summary>
/// Runtime-neutral deterministic verdict supplied to advisory analysis.
/// It intentionally avoids exposing the budget checker's internal comparison model
/// across the AI provider boundary.
/// </summary>
public sealed record PerformanceRegressionResult(
    string BenchmarkName,
    bool Passed,
    bool MeanExceeded,
    bool AllocationExceeded);
