namespace PerformanceAgent.Core.History;

public sealed record ResolvedBaselines(
    string? AnchorRunId,
    string? CurrentRunId);

public sealed class BaselineResolver
{
    public ResolvedBaselines Resolve(BenchmarkHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        string? anchor = null;
        string? current = null;

        foreach (var baselineEvent in history.BaselineEvents.OrderBy(item => item.Timestamp))
        {
            if (!history.Runs.Any(run => string.Equals(run.RunId, baselineEvent.RunId, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Baseline event '{baselineEvent.EventId}' references unknown run '{baselineEvent.RunId}'.");

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
