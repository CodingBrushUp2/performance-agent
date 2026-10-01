namespace PerformanceAgent.Core.Analysis;

public sealed record PerformanceAnalysis(
    string Summary,
    IReadOnlyList<AnalysisEvidenceReference> EvidenceReferences,
    IReadOnlyList<PerformanceHypothesis> Hypotheses,
    IReadOnlyList<SuggestedExperiment> SuggestedExperiments,
    string? Uncertainty)
{
    public PerformanceAnalysis Validate()
    {
        if (string.IsNullOrWhiteSpace(Summary))
            throw new InvalidOperationException("AI analysis summary is required.");

        ArgumentNullException.ThrowIfNull(EvidenceReferences);
        ArgumentNullException.ThrowIfNull(Hypotheses);
        ArgumentNullException.ThrowIfNull(SuggestedExperiments);

        if (EvidenceReferences.Any(x => string.IsNullOrWhiteSpace(x.BenchmarkName) || string.IsNullOrWhiteSpace(x.Observation)))
            throw new InvalidOperationException("AI evidence references require a benchmark name and observation.");

        if (Hypotheses.Any(x => string.IsNullOrWhiteSpace(x.Statement)))
            throw new InvalidOperationException("AI hypotheses require a statement.");

        if (SuggestedExperiments.Any(x => string.IsNullOrWhiteSpace(x.Description)))
            throw new InvalidOperationException("AI suggested experiments require a description.");

        return this;
    }
}

public sealed record AnalysisEvidenceReference(
    string BenchmarkName,
    string Observation);

public sealed record PerformanceHypothesis(
    string Statement,
    string? Rationale = null);

public sealed record SuggestedExperiment(
    string Description,
    string? ExpectedSignal = null);
