using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class BaselineHistoryIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-09-30T12:00:00Z");

    [Theory]
    [InlineData(BaselineKind.Current, "current.json")]
    [InlineData(BaselineKind.Anchor, "anchor.json")]
    public async Task Get_EventOverridesStaleMissingAndCorruptPointers(BaselineKind kind, string fileName)
    {
        await Archive("run-old");
        await Archive("run-new");
        var store = new FileBaselineStore(_root);
        await store.SetAsync(kind, "run-old");
        await Events().AppendAsync(new("event-1", Timestamp, kind, BaselineEventType.Reset, "run-new", "run-old", null));
        var path = Path.Combine(_root, "baselines", fileName);

        Assert.Equal("run-new", (await store.GetAsync(kind))?.RunId);
        File.Delete(path);
        Assert.Equal("run-new", (await store.GetAsync(kind))?.RunId);
        await File.WriteAllTextAsync(path, "broken pointer");
        Assert.Equal("run-new", (await store.GetAsync(kind))?.RunId);
    }

    [Fact]
    public async Task Get_PreservesLegacyPointerForKindWithoutEvents()
    {
        await Archive("run-old");
        await Archive("run-new");
        var store = new FileBaselineStore(_root);
        await store.SetAsync(BaselineKind.Anchor, "run-old");
        await Events().AppendAsync(new("event-1", Timestamp, BaselineKind.Current, BaselineEventType.Created, "run-new", null, null));
        Assert.Equal("run-old", (await store.GetAsync(BaselineKind.Anchor))?.RunId);
        Assert.Equal("run-new", (await store.GetAsync(BaselineKind.Current))?.RunId);
    }

    [Fact]
    public async Task Get_UsesAppendOrderEvenWhenClockMovesBackwards()
    {
        var first = await Archive("run-old");
        var second = await Archive("run-new");
        await Events().AppendAsync(new("event-1", Timestamp, BaselineKind.Current, BaselineEventType.Created, "run-old", null, null));
        await Events().AppendAsync(new("event-2", Timestamp.AddHours(-1), BaselineKind.Current, BaselineEventType.Promoted, "run-new", "run-old", null));
        var events = await Events().ReadAllAsync();
        var resolved = new BaselineResolver().Resolve(new("1.0", [first, second], events));
        Assert.Equal("run-new", resolved.CurrentRunId);
        Assert.Equal(resolved.CurrentRunId, (await new FileBaselineStore(_root).GetAsync(BaselineKind.Current))?.RunId);
    }

    [Fact]
    public async Task Get_MissingArchivedEventRunDoesNotFallBackToPointer()
    {
        await Archive("run-old");
        var store = new FileBaselineStore(_root);
        await store.SetAsync(BaselineKind.Current, "run-old");
        await Events().AppendAsync(new("event-1", Timestamp, BaselineKind.Current, BaselineEventType.Reset, "run-missing", "run-old", null));
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.GetAsync(BaselineKind.Current));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"eventId\":\"event-1\",\"runId\":\"run-1\",\"kind\":99,\"type\":0}")]
    public async Task Get_InvalidHistoryDoesNotFallBackToPointer(string json)
    {
        await Archive("run-old");
        var store = new FileBaselineStore(_root);
        await store.SetAsync(BaselineKind.Current, "run-old");
        await File.WriteAllTextAsync(Path.Combine(_root, "baseline-events.jsonl"), json);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync(BaselineKind.Current));
    }

    private FileBaselineEventStore Events() => new(_root);

    private async Task<ArchivedBenchmarkRun> Archive(string id)
    {
        var run = new ArchivedBenchmarkRun(id, Timestamp, null, new BenchmarkEvidence("1.0", []));
        await new FileRunArchive(_root).AppendAsync(run);
        return run;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
