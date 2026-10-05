using System.Text.Json;
using System.Text.Json.Serialization;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Verdicts;

namespace PerformanceAgent.Cli;

internal sealed record CheckVerdictReference(string Kind, string? Id);

internal sealed record CheckVerdictInput(
    CheckVerdictReference Reference,
    EvidenceCheckResult Check);

internal sealed record CheckVerdictBudget(
    double? MaxMeanRegressionPercent,
    double? MaxAllocationRegressionPercent);

internal sealed record CheckVerdictMetric(
    double? Baseline,
    double? Candidate,
    double? PercentChange,
    ComparisonStatus Status,
    bool BudgetExceeded);

internal sealed record CheckVerdictBenchmark(
    string Name,
    PerformanceVerdict Verdict,
    IReadOnlyList<string> Reasons,
    CheckVerdictMetric Mean,
    CheckVerdictMetric Allocation);

internal sealed record CheckVerdictEntry(
    CheckVerdictReference Reference,
    PerformanceVerdict Verdict,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<CheckVerdictBenchmark> Benchmarks);

internal sealed record CheckVerdictDocument(
    string SchemaVersion,
    PerformanceVerdict Verdict,
    string Candidate,
    CheckVerdictBudget Budget,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<CheckVerdictEntry> Checks);

internal sealed class CheckVerdictJsonWriter
{
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Write(
        string candidate,
        PerformanceBudget budget,
        PerformanceVerdict overallVerdict,
        IReadOnlyList<CheckVerdictInput> checks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(checks);
        if (checks.Count == 0)
            throw new ArgumentException("At least one performance check is required.", nameof(checks));

        var entries = checks.Select(Map).ToArray();
        var document = new CheckVerdictDocument(
            SchemaVersion,
            overallVerdict,
            candidate,
            new CheckVerdictBudget(
                budget.MaxMeanRegressionPercent,
                budget.MaxAllocationRegressionPercent),
            entries
                .SelectMany(x => x.Reasons)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            entries);

        return JsonSerializer.Serialize(document, Options);
    }

    private static CheckVerdictEntry Map(CheckVerdictInput input) =>
        new(
            input.Reference,
            input.Check.Verdict,
            input.Check.Reasons,
            input.Check.Benchmarks.Select(Map).ToArray());

    private static CheckVerdictBenchmark Map(BenchmarkCheckResult item) =>
        new(
            item.Name,
            item.Verdict,
            item.Reasons,
            Map(item.Result.Comparison.Mean, item.Result.MeanExceeded),
            Map(item.Result.Comparison.AllocatedBytes, item.Result.AllocationExceeded));

    private static CheckVerdictMetric Map(MetricChange change, bool budgetExceeded) =>
        new(
            change.Baseline,
            change.Candidate,
            change.PercentChange,
            change.Status,
            budgetExceeded);
}
