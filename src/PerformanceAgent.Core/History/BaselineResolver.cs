namespace PerformanceAgent.Core.History;

public sealed record ResolvedBaselines(
    string? AnchorRunId,
    string? CurrentRunId);

public sealed class BaselineResolver
{
    public ResolvedBaselines Resolve(BenchmarkHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        BaselineEventValidation.Validate(history.BaselineEvents);
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in history.Runs)
        {
            if (!runIds.Add(run.RunId))
                throw new InvalidOperationException($"Duplicate archived RunId '{run.RunId}'.");
        }

        string? anchor = null;
        string? current = null;

        foreach (var baselineEvent in history.BaselineEvents)
        {
            if (!runIds.Contains(baselineEvent.RunId))
                throw new InvalidOperationException($"Baseline event '{baselineEvent.EventId}' references unknown run '{baselineEvent.RunId}'.");

            if (baselineEvent.PreviousRunId is not null && !runIds.Contains(baselineEvent.PreviousRunId))
                throw new InvalidOperationException($"Baseline event '{baselineEvent.EventId}' references unknown previous run '{baselineEvent.PreviousRunId}'.");

            switch (baselineEvent.Kind)
            {
                case BaselineKind.Anchor:
                    anchor = baselineEvent.RunId;
                    break;
                case BaselineKind.Current:
                    current = baselineEvent.RunId;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported baseline kind: {baselineEvent.Kind}.");
            }
        }

        return new ResolvedBaselines(anchor, current);
    }
}
