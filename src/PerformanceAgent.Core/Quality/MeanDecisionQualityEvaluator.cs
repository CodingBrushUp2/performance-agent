using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Quality;

public sealed class MeanDecisionQualityEvaluator
{
    public const double RequiredConfidenceLevelPercent = 99.9;

    public MeanDecisionQuality Evaluate(
        BenchmarkMeasurement baseline,
        BenchmarkMeasurement candidate,
        double? maxMeanRegressionPercent)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        if (maxMeanRegressionPercent is null)
        {
            return new MeanDecisionQuality(
                MeanDecisionQualityStatus.NotConfigured,
                null,
                null,
                null,
                null);
        }

        if (baseline == candidate)
        {
            return new MeanDecisionQuality(
                MeanDecisionQualityStatus.ConclusiveWithinBudget,
                0,
                0,
                baseline.Statistics?.ConfidenceLevelPercent,
                null);
        }

        if (baseline.MeanNanoseconds <= 0)
        {
            return Inconclusive("Baseline mean must be greater than zero to evaluate statistical decision quality.");
        }

        if (baseline == candidate)
        {
            return new MeanDecisionQuality(
                MeanDecisionQualityStatus.ConclusiveWithinBudget,
                0,
                0,
                baseline.Statistics?.ConfidenceLevelPercent,
                null);
        }

        if (baseline.Statistics is null || candidate.Statistics is null)
        {
            return Inconclusive(
                "Benchmark statistics are unavailable. Re-run baseline and candidate with the current Performance Agent before trusting the mean verdict.");
        }

        var baselineLower = baseline.Statistics.ConfidenceIntervalLowerNanoseconds;
        var baselineUpper = baseline.Statistics.ConfidenceIntervalUpperNanoseconds;
        var candidateLower = candidate.Statistics.ConfidenceIntervalLowerNanoseconds;
        var candidateUpper = candidate.Statistics.ConfidenceIntervalUpperNanoseconds;
        var baselineConfidenceLevel = baseline.Statistics.ConfidenceLevelPercent;
        var candidateConfidenceLevel = candidate.Statistics.ConfidenceLevelPercent;

        if (baselineLower is null || baselineUpper is null || candidateLower is null || candidateUpper is null)
        {
            return Inconclusive(
                "Benchmark confidence interval is unavailable. Re-run baseline and candidate with the current Performance Agent before trusting the mean verdict.");
        }

        if (baselineConfidenceLevel is null
            || candidateConfidenceLevel is null
            || Math.Abs(baselineConfidenceLevel.Value - RequiredConfidenceLevelPercent) > 1e-9
            || Math.Abs(candidateConfidenceLevel.Value - RequiredConfidenceLevelPercent) > 1e-9)
        {
            return Inconclusive(
                $"A {RequiredConfidenceLevelPercent:0.0}% BenchmarkDotNet confidence interval is required for a trusted mean verdict.");
        }

        if (baselineLower <= 0
            || baselineUpper <= 0
            || candidateLower < 0
            || candidateUpper < 0
            || baselineLower > baselineUpper
            || candidateLower > candidateUpper)
        {
            return Inconclusive("Benchmark confidence interval is not usable for a ratio-based regression decision.");
        }

        var minimumRegressionPercent =
            ((candidateLower.Value / baselineUpper.Value) - 1d) * 100d;
        var maximumRegressionPercent =
            ((candidateUpper.Value / baselineLower.Value) - 1d) * 100d;

        if (!double.IsFinite(minimumRegressionPercent) || !double.IsFinite(maximumRegressionPercent))
        {
            return Inconclusive("Benchmark confidence interval produced a non-finite regression range.");
        }

        if (minimumRegressionPercent > maxMeanRegressionPercent.Value)
        {
            return new MeanDecisionQuality(
                MeanDecisionQualityStatus.ConclusiveExceededBudget,
                minimumRegressionPercent,
                maximumRegressionPercent,
                RequiredConfidenceLevelPercent,
                null);
        }

        if (maximumRegressionPercent <= maxMeanRegressionPercent.Value)
        {
            return new MeanDecisionQuality(
                MeanDecisionQualityStatus.ConclusiveWithinBudget,
                minimumRegressionPercent,
                maximumRegressionPercent,
                RequiredConfidenceLevelPercent,
                null);
        }

        return new MeanDecisionQuality(
            MeanDecisionQualityStatus.Inconclusive,
            minimumRegressionPercent,
            maximumRegressionPercent,
            RequiredConfidenceLevelPercent,
            $"The {RequiredConfidenceLevelPercent:0.0}% confidence range for mean regression crosses the configured budget of {maxMeanRegressionPercent.Value:0.##}%.");
    }

    private static MeanDecisionQuality Inconclusive(string reason) =>
        new(
            MeanDecisionQualityStatus.Inconclusive,
            null,
            null,
            RequiredConfidenceLevelPercent,
            reason);
}
