using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Quality;
using PerformanceAgent.Core.Verdicts;

namespace PerformanceAgent.Cli;

internal sealed record BenchmarkCheckResult(
    string Name,
    BudgetCheckResult Result,
    MeanDecisionQuality MeanDecisionQuality,
    PerformanceVerdict Verdict,
    IReadOnlyList<string> Reasons)
{
    public bool Passed => Verdict == PerformanceVerdict.Pass;
}

internal sealed record EvidenceCheckResult(
    PerformanceVerdict Verdict,
    IReadOnlyList<BenchmarkCheckResult> Benchmarks,
    IReadOnlyList<string> Reasons)
{
    public bool Passed => Verdict == PerformanceVerdict.Pass;
}

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
        {
            return new EvidenceCheckResult(
                PerformanceVerdict.Inconclusive,
                [],
                environment.Differences
                    .Select(x => $"Environment is not comparable: {x}.")
                    .ToArray());
        }

        var baselineByName = baseline.Measurements.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var candidateByName = candidate.Measurements.ToDictionary(x => x.Name, StringComparer.Ordinal);

        var missingFromCandidate = baselineByName.Keys
            .Except(candidateByName.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingFromBaseline = candidateByName.Keys
            .Except(baselineByName.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (missingFromCandidate.Length != 0 || missingFromBaseline.Length != 0)
        {
            var reasons = new List<string>();
            if (missingFromCandidate.Length != 0)
                reasons.Add($"Candidate evidence is missing benchmark(s): {string.Join(", ", missingFromCandidate)}.");
            if (missingFromBaseline.Length != 0)
                reasons.Add($"Baseline evidence is missing benchmark(s): {string.Join(", ", missingFromBaseline)}.");

            return new EvidenceCheckResult(
                PerformanceVerdict.Inconclusive,
                [],
                reasons);
        }

        var checker = new PerformanceBudgetChecker();
        var qualityEvaluator = new MeanDecisionQualityEvaluator();
        var results = baselineByName.Keys
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                var baselineMeasurement = baselineByName[name];
                var candidateMeasurement = candidateByName[name];
                return CreateBenchmarkCheck(
                    name,
                    baselineMeasurement,
                    candidateMeasurement,
                    checker.Check(baselineMeasurement, candidateMeasurement, budget),
                    qualityEvaluator.Evaluate(
                        baselineMeasurement,
                        candidateMeasurement,
                        budget.MaxMeanRegressionPercent),
                    budget);
            })
            .ToArray();

        var verdict = results.Any(x => x.Verdict == PerformanceVerdict.Fail)
            ? PerformanceVerdict.Fail
            : results.Any(x => x.Verdict == PerformanceVerdict.Inconclusive)
                ? PerformanceVerdict.Inconclusive
                : PerformanceVerdict.Pass;

        return new EvidenceCheckResult(
            verdict,
            results,
            results.SelectMany(x => x.Reasons).ToArray());
    }

    private static BenchmarkCheckResult CreateBenchmarkCheck(
        string name,
        BenchmarkMeasurement baseline,
        BenchmarkMeasurement candidate,
        BudgetCheckResult result,
        MeanDecisionQuality meanDecisionQuality,
        PerformanceBudget budget)
    {
        var reasons = new List<string>();

        if (meanDecisionQuality.IsExceeded)
        {
            reasons.Add(
                $"{name}: mean regression exceeds the configured budget with a conclusive {MeanDecisionQualityEvaluator.RequiredConfidenceLevelPercent:0.0}% confidence range.");
        }

        if (result.AllocationExceeded)
            reasons.Add($"{name}: allocation regression exceeds the configured budget.");

        if (meanDecisionQuality.IsExceeded || result.AllocationExceeded)
        {
            return new BenchmarkCheckResult(
                name,
                result,
                meanDecisionQuality,
                PerformanceVerdict.Fail,
                reasons);
        }

        var meanInconclusiveReasonAdded = AddInconclusiveReason(
            reasons,
            name,
            "mean",
            result.Comparison.Mean,
            budget.MaxMeanRegressionPercent);

        if (!meanInconclusiveReasonAdded
            && meanDecisionQuality.IsInconclusive
            && meanDecisionQuality.Reason is not null)
        {
            reasons.Add($"{name}: {meanDecisionQuality.Reason}");
        }

        AddInconclusiveReason(
            reasons,
            name,
            "allocation",
            result.Comparison.AllocatedBytes,
            budget.MaxAllocationRegressionPercent);

        return new BenchmarkCheckResult(
            name,
            result,
            meanDecisionQuality,
            reasons.Count == 0 ? PerformanceVerdict.Pass : PerformanceVerdict.Inconclusive,
            reasons);
    }

    private static bool AddInconclusiveReason(
        ICollection<string> reasons,
        string benchmarkName,
        string metricName,
        MetricChange change,
        double? configuredThreshold)
    {
        if (configuredThreshold is null)
            return false;

        if (change.Status != ComparisonStatus.Comparable || change.PercentChange is null)
        {
            reasons.Add($"{benchmarkName}: required {metricName} metric is not comparable ({change.Status}).");
            return true;
        }

        return false;
    }
}
