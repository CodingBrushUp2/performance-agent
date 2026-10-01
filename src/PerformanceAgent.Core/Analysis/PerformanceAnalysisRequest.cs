using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;

namespace PerformanceAgent.Core.Analysis;

public sealed record PerformanceAnalysisRequest(
    BenchmarkEvidence Baseline,
    BenchmarkEvidence Candidate,
    PerformanceBudget Budget)
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

        return this;
    }
}
