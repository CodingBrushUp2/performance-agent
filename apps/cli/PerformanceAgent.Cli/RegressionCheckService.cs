using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Cli;

internal sealed record BenchmarkCheckResult(
    string Name,
    BudgetCheckResult Result);

internal sealed record EvidenceCheckResult(
    bool Passed,
    IReadOnlyList<BenchmarkCheckResult> Benchmarks);

internal sealed class RegressionCheckService
{
    public EvidenceCheckResult Check(
        BenchmarkEvidence baseline,
        BenchmarkEvidence candidate,
        PerformanceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(budget);

        var environment = new BenchmarkEnvironmentComparer().Compare(
            baseline.Environment,
            candidate.Environment);
        if (!environment.IsComparable)
            throw new InvalidOperationException(
                $"Baseline and candidate benchmark environments are not comparable: {string.Join("; ", environment.Differences)}");

        var baselineByName = baseline.Measurements.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var candidateByName = candidate.Measurements.ToDictionary(x => x.Name, StringComparer.Ordinal);

        if (baselineByName.Keys.Except(candidateByName.Keys, StringComparer.Ordinal).Any()
            || candidateByName.Keys.Except(baselineByName.Keys, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Baseline and candidate benchmark identities do not match.");

        var checker = new PerformanceBudgetChecker();
        var results = baselineByName.Keys
            .Order(StringComparer.Ordinal)
            .Select(name => new BenchmarkCheckResult(
                name,
                checker.Check(baselineByName[name], candidateByName[name], budget)))
            .ToArray();

        return new EvidenceCheckResult(results.All(x => x.Result.Passed), results);
    }
}
