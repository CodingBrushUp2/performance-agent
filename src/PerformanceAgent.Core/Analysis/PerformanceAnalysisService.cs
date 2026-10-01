using PerformanceAgent.Core.Evidence;

namespace PerformanceAgent.Core.Analysis;

/// <summary>
/// Runs advisory analysis over already-measured evidence. It has no access to evidence archives,
/// baseline stores, or regression checks, so it cannot change measured facts or verdicts.
/// </summary>
public sealed class PerformanceAnalysisService
{
    private readonly IPerformanceAnalysisProvider _provider;

    public PerformanceAnalysisService(IPerformanceAnalysisProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public async Task<PerformanceAnalysis> AnalyzeAsync(
        PerformanceAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var analysis = await _provider.AnalyzeAsync(ReadOnlySnapshot(request), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("AI analysis provider returned no analysis.");

        analysis.Validate();
        ValidateEvidenceReferences(analysis, request);
        return analysis;
    }

    private static void ValidateEvidenceReferences(PerformanceAnalysis analysis, PerformanceAnalysisRequest request)
    {
        var measured = request.Baseline.Measurements
            .Concat(request.Candidate.Measurements)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = analysis.EvidenceReferences.FirstOrDefault(x => !measured.Contains(x.BenchmarkName));
        if (unknown is not null)
            throw new InvalidOperationException(
                $"AI evidence reference '{unknown.BenchmarkName}' does not identify a benchmark in the supplied evidence.");
    }

    // Providers receive read-only copies of the collections so they cannot mutate caller-owned evidence or verdicts.
    private static PerformanceAnalysisRequest ReadOnlySnapshot(PerformanceAnalysisRequest request) => request with
    {
        Baseline = ReadOnlySnapshot(request.Baseline),
        Candidate = ReadOnlySnapshot(request.Candidate),
        RegressionResults = request.RegressionResults is null
            ? null
            : Array.AsReadOnly(request.RegressionResults.ToArray()),
    };

    private static BenchmarkEvidence ReadOnlySnapshot(BenchmarkEvidence evidence) => evidence with
    {
        Measurements = Array.AsReadOnly(evidence.Measurements.ToArray()),
    };
}
