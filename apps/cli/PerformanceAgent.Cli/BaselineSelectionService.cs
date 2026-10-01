using PerformanceAgent.Core.History;

namespace PerformanceAgent.Cli;

internal sealed class BaselineSelectionService
{
    private readonly WorkspaceStorage _storage;

    public BaselineSelectionService(WorkspaceStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task SetAsync(
        BaselineKind kind,
        string runId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _storage.EnsureWritable();

        var archive = new FileRunArchive(_storage.StateDirectory);
        _ = await archive.ReadAsync(runId, cancellationToken);

        var store = new FileBaselineStore(_storage.StateDirectory);
        var previous = await store.GetAsync(kind, cancellationToken);
        var eventType = previous is null ? BaselineEventType.Created : BaselineEventType.Reset;

        await new FileBaselineEventStore(_storage.StateDirectory).AppendAsync(
            new BaselineEvent(
                $"event-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow,
                kind,
                eventType,
                runId,
                previous?.RunId,
                reason),
            cancellationToken);

        await store.SetAsync(kind, runId, cancellationToken);
    }
}
