namespace PerformanceAgent.Core.History;

public enum BaselineKind
{
    Anchor,
    Current
}

public enum BaselineEventType
{
    Created,
    Promoted,
    Reset
}

public sealed record BaselineEvent(
    string EventId,
    DateTimeOffset Timestamp,
    BaselineKind Kind,
    BaselineEventType Type,
    string RunId,
    string? PreviousRunId,
    string? Reason);
