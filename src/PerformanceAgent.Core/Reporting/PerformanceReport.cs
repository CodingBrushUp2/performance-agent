using PerformanceAgent.Core.Comparison;

namespace PerformanceAgent.Core.Reporting;

public sealed record PerformanceReport(
    string SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ComparisonResult> Comparisons);
