using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Reporting;

namespace PerformanceAgent.Cli;

internal sealed record HtmlReportResult(string Html, bool Passed);

internal sealed class HtmlReportService(WorkspaceStorage storage)
{
    public async Task<HtmlReportResult> CreateAsync(string candidateRunId, string? baselineRunId = null,
        string? budgetPath = null, CancellationToken cancellationToken = default)
    {
        if (baselineRunId is null)
        {
            var current = await new FileBaselineStore(storage.StateDirectory).GetAsync(BaselineKind.Current, cancellationToken);
            baselineRunId = current?.RunId ?? throw new InvalidOperationException(
                "No current baseline is configured. Use 'perfagent baseline set <run-id>' or 'report <candidate-run-id> --baseline <run-id>'.");
        }
        var archive = new FileRunArchive(storage.StateDirectory);
        var baseline = await archive.ReadAsync(baselineRunId, cancellationToken);
        var candidate = await archive.ReadAsync(candidateRunId, cancellationToken);
        // Apply the same normalized-evidence validation as evidence-file CLI checks.
        var reader = new JsonBenchmarkEvidenceReader();
        var writer = new JsonBenchmarkEvidenceWriter();
        var baselineEvidence = reader.Read(writer.Write(baseline.Evidence));
        var candidateEvidence = reader.Read(writer.Write(candidate.Evidence));
        PerformanceBudget budget;
        string source;
        if (budgetPath is null)
        {
            var configuration = new WorkspaceConfiguration(storage).InspectBudget();
            budget = configuration.Budget;
            source = configuration.BudgetSource == "perfagent.json" ? configuration.Path : configuration.BudgetSource;
        }
        else
        {
            source = Path.GetFullPath(budgetPath, storage.WorkspaceDirectory);
            budget = new JsonPerformanceBudgetReader().Read(await File.ReadAllTextAsync(source, cancellationToken));
        }
        var check = new RegressionCheckService().Check(baselineEvidence, candidateEvidence, budget);
        var report = new PerformanceReport("1.0", DateTimeOffset.UtcNow, check.Benchmarks.Select(x => x.Result.Comparison).ToArray());
        return new(new HtmlPerformanceReportWriter().Write(report,
            new PerformanceReportContext(baseline, candidate, budget, source, check.Passed)), check.Passed);
    }
}
