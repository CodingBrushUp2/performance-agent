using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;

namespace PerformanceAgent.Core.Analysis;

/// <summary>
/// Normalized evidence plus the deterministic regression context an analysis provider may explain.
/// </summary>
/// <param name="RegressionResults">
/// Deterministic per-benchmark budget results already computed by the regression check.
/// Providers may reference these verdicts but can never change them.
/// </param>
public sealed record PerformanceAnalysisRequest(
    BenchmarkEvidence Baseline,
    BenchmarkEvidence Candidate,
    PerformanceBudget Budget,
    IReadOnlyList<PerformanceRegressionResult>? RegressionResults = null)
{
    public PerformanceAnalysisRequest Validate()
    {
        ArgumentNullException.ThrowIfNull(Baseline);
        ArgumentNullException.ThrowIfNull(Candidate);
        ArgumentNullException.ThrowIfNull(Budget);

        if (Baseline.Measurements.Count == 0)
            throw new ArgumentException("Baseline evidence must contain at least one measurement.", nameof(Baseline));

        if (Candidate.Measurements.Count == 0)
            throw new ArgumentException("Candidate evidence must contain at least one measurement.", nameof(Candidate));

        if (RegressionResults is not null)
            ValidateRegressionResults(RegressionResults);

        return this;
    }

    private void ValidateRegressionResults(IReadOnlyList<PerformanceRegressionResult> results)
    {
        var baselineNames = Baseline.Measurements.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var candidateNames = Candidate.Measurements.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in results)
        {
            if (result is null)
                throw new ArgumentException("Regression results must not contain null entries.", nameof(RegressionResults));

            if (!baselineNames.Contains(result.BenchmarkName) || !candidateNames.Contains(result.BenchmarkName))
                throw new ArgumentException(
                    $"Regression result '{result.BenchmarkName}' does not match a benchmark in both baseline and candidate evidence.",
                    nameof(RegressionResults));

            if (!seen.Add(result.BenchmarkName))
                throw new ArgumentException(
                    $"Regression results contain duplicate benchmark '{result.BenchmarkName}'.",
                    nameof(RegressionResults));
        }
    }
}
