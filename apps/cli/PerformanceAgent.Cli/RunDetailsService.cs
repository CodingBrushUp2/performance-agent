using PerformanceAgent.Core.History;

namespace PerformanceAgent.Cli;

internal sealed record RunDetails(ArchivedBenchmarkRun Run, bool IsCurrent, bool IsAnchor);

// Shared read use case for CLI history <run-id> and the optional local UI.
internal sealed class RunDetailsService(WorkspaceStorage storage)
{
    public async Task<RunDetails> ReadAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await new FileRunArchive(storage.StateDirectory).ReadAsync(runId, cancellationToken);
        if (run.Evidence?.Measurements is null)
            throw new InvalidOperationException($"Archived benchmark run '{runId}' has no benchmark evidence.");
        var baselines = new FileBaselineStore(storage.StateDirectory);
        var current = await baselines.GetAsync(BaselineKind.Current, cancellationToken);
        var anchor = await baselines.GetAsync(BaselineKind.Anchor, cancellationToken);
        return new(run, runId == current?.RunId, runId == anchor?.RunId);
    }
}
