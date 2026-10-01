using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;

namespace PerformanceAgent.Cli;

/// <summary>Measured, authoritative input to analysis. Computed before any AI provider is created.</summary>
internal sealed record DeterministicAnalysisResult(
    string CandidateRunId,
    string BaselineRunId,
    PerformanceBudget Budget,
    string BudgetSource,
    EvidenceCheckResult Check);

/// <summary>
/// Outcome of <c>perfagent analyze</c>. The deterministic verdict is fixed before analysis and is never
/// changed by it; an AI failure is recorded separately and only fails the analysis.
/// </summary>
internal sealed record AnalyzeResult(
    DeterministicAnalysisResult Deterministic,
    PerformanceAnalysis? Analysis,
    string? AnalysisError)
{
    /// <summary>2 when analysis failed; otherwise the deterministic verdict decides (0 PASS, 1 REGRESSION).</summary>
    public int ExitCode => AnalysisError is not null ? 2 : Deterministic.Check.Passed ? 0 : 1;
}

/// <summary>
/// Use case behind <c>perfagent analyze &lt;candidate-run-id&gt;</c>: archived candidate + Current baseline +
/// workspace budget -> <see cref="RegressionCheckService"/> -> <see cref="PerformanceAnalysisService"/>.
/// It only reads workspace state.
/// </summary>
internal sealed class AnalyzeService(
    WorkspaceStorage storage,
    Func<string, string?, IPerformanceAnalysisProvider>? createProvider = null)
{
    private readonly Func<string, string?, IPerformanceAnalysisProvider> _createProvider =
        createProvider ?? AnalysisProviderFactory.Create;

    public async Task<AnalyzeResult> AnalyzeAsync(string candidateRunId, CancellationToken cancellationToken)
    {
        var configuration = new WorkspaceConfiguration(storage).Inspect();
        var archive = new FileRunArchive(storage.StateDirectory);
        var candidate = await archive.ReadAsync(candidateRunId, cancellationToken);
        var current = await new FileBaselineStore(storage.StateDirectory).GetAsync(BaselineKind.Current, cancellationToken)
            ?? throw new InvalidOperationException(
                "AI analysis requires a Current baseline, and none is configured. Set one with 'perfagent baseline set <run-id>'. The Anchor baseline is not used as a fallback.");
        var baseline = await archive.ReadAsync(current.RunId, cancellationToken);

        // Apply the same normalized-evidence validation as evidence-file CLI checks and reports.
        var reader = new JsonBenchmarkEvidenceReader();
        var writer = new JsonBenchmarkEvidenceWriter();
        var baselineEvidence = reader.Read(writer.Write(baseline.Evidence));
        var candidateEvidence = reader.Read(writer.Write(candidate.Evidence));

        var check = new RegressionCheckService().Check(baselineEvidence, candidateEvidence, configuration.Budget);
        var deterministic = new DeterministicAnalysisResult(
            candidate.RunId,
            baseline.RunId,
            configuration.Budget,
            configuration.BudgetSource == "perfagent.json" ? configuration.Path : configuration.BudgetSource,
            check);

        var request = new PerformanceAnalysisRequest(
            baselineEvidence,
            candidateEvidence,
            configuration.Budget,
            check.Benchmarks
                .Select(x => new PerformanceRegressionResult(x.Name, x.Result.Passed, x.Result.MeanExceeded, x.Result.AllocationExceeded))
                .ToArray());

        try
        {
            var provider = _createProvider(configuration.AiProvider, configuration.AiModel);
            try
            {
                var analysis = await new PerformanceAnalysisService(provider).AnalyzeAsync(request, cancellationToken);
                return new(deterministic, analysis, null);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or TimeoutException)
        {
            // Provider configuration, credential, rate-limit, outage, timeout, or malformed output.
            return new(deterministic, null, exception.Message);
        }
    }
}
