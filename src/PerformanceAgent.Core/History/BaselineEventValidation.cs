namespace PerformanceAgent.Core.History;

internal static class BaselineEventValidation
{
    public static void Validate(IReadOnlyList<BaselineEvent> events)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var selections = new Dictionary<BaselineKind, string>();
        foreach (var item in events)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.EventId)
                || !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Type))
                throw new InvalidOperationException("Baseline event history contains an invalid event.");
            try
            {
                RunIdValidation.Validate(item.RunId);
                if (item.PreviousRunId is not null)
                    RunIdValidation.Validate(item.PreviousRunId);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"Baseline event '{item.EventId}' contains an invalid RunId.", exception);
            }
            if (!ids.Add(item.EventId))
                throw new InvalidOperationException($"Duplicate baseline event ID '{item.EventId}'.");
            if (item.Type == BaselineEventType.Created && (item.PreviousRunId is not null || selections.ContainsKey(item.Kind)))
                throw new InvalidOperationException($"Baseline event '{item.EventId}' cannot recreate an existing {item.Kind} baseline.");
            if (selections.TryGetValue(item.Kind, out var previous)
                && !string.Equals(previous, item.PreviousRunId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Baseline event '{item.EventId}' has previous RunId '{item.PreviousRunId ?? "-"}', expected '{previous}'.");

            // A first Reset/Promoted event can reference a legacy pointer-only baseline.
            selections[item.Kind] = item.RunId;
        }
    }
}
