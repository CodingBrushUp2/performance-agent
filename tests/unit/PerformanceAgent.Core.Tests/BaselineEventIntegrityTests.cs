using System.Text.Json;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class BaselineEventIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-09-30T12:00:00Z");
    private string Log => Path.Combine(_root, "baseline-events.jsonl");
    private FileBaselineEventStore Store => new(_root);
    private static BaselineEvent Event(string id, string run, string? previous = null,
        BaselineEventType type = BaselineEventType.Created, BaselineKind kind = BaselineKind.Current) =>
        new(id, Timestamp, kind, type, run, previous, null);

    private async Task Initialize()
    {
        foreach (var id in new[] { "a", "b", "c" })
            await new FileRunArchive(_root).AppendAsync(new(id, Timestamp, null, new BenchmarkEvidence("1.0", [])));
        await Store.AppendAsync(Event("first", "a"));
    }

    [Fact]
    public async Task DuplicateId_IsRejectedWithoutChangingHistory()
    {
        await Initialize();
        var before = await File.ReadAllBytesAsync(Log);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store.AppendAsync(Event("first", "b", "a", BaselineEventType.Reset)));
        Assert.Equal(before, await File.ReadAllBytesAsync(Log));
        await File.AppendAllTextAsync(Log, Serialize(Event("first", "b", "a", BaselineEventType.Reset)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Store.ReadAllAsync());
        Assert.Contains("Duplicate baseline event ID", error.Message);
    }

    [Theory]
    [InlineData(BaselineEventType.Created, null)]
    [InlineData(BaselineEventType.Reset, null)]
    [InlineData(BaselineEventType.Reset, "c")]
    [InlineData(BaselineEventType.Promoted, "c")]
    public async Task ContradictoryTransitions_AreRejected(BaselineEventType type, string? previous)
    {
        await Initialize();
        var before = await File.ReadAllBytesAsync(Log);
        var invalid = Event("second", "b", previous, type);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store.AppendAsync(invalid));
        Assert.Equal(before, await File.ReadAllBytesAsync(Log));
        await File.AppendAllTextAsync(Log, Serialize(invalid));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileBaselineStore(_root).GetAsync(BaselineKind.Current));
    }

    [Theory]
    [InlineData("missing", "a")]
    [InlineData("b", "missing")]
    public async Task MissingReferences_AreRejectedBeforeAppend(string run, string previous)
    {
        await Initialize();
        var before = await File.ReadAllBytesAsync(Log);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Store.AppendAsync(Event("second", run, previous, BaselineEventType.Reset)));
        Assert.Equal(before, await File.ReadAllBytesAsync(Log));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public async Task Get_ValidatesSupersededAndPreviousReferences(string deleted)
    {
        await Initialize();
        await Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Reset));
        await Store.AppendAsync(Event("third", "c", "b", BaselineEventType.Promoted));
        File.Delete(Path.Combine(_root, "archive", deleted + ".json"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => new FileBaselineStore(_root).GetAsync(BaselineKind.Current));
    }

    [Fact]
    public async Task EqualTimestamps_UseAppendOrderAndKeepKindsIndependent()
    {
        await Initialize();
        await Store.AppendAsync(Event("anchor", "a", kind: BaselineKind.Anchor));
        await Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Promoted));
        await Store.AppendAsync(Event("third", "c", "b", BaselineEventType.Reset));
        var baselines = new FileBaselineStore(_root);
        Assert.Equal("c", (await baselines.GetAsync(BaselineKind.Current))?.RunId);
        Assert.Equal("a", (await baselines.GetAsync(BaselineKind.Anchor))?.RunId);
    }

    [Fact]
    public async Task CompletedFinalLineWithoutNewline_IsSafelySeparatedOnAppend()
    {
        await Initialize();
        await File.WriteAllTextAsync(Log, (await File.ReadAllTextAsync(Log)).TrimEnd());
        var before = await File.ReadAllTextAsync(Log);
        await Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Reset));
        Assert.StartsWith(before + "\n", await File.ReadAllTextAsync(Log));
        Assert.Equal(2, (await Store.ReadAllAsync()).Count);
    }

    [Fact]
    public async Task CorruptTail_BlocksAppendWithoutAlteringExistingBytes()
    {
        await Initialize();
        await File.AppendAllTextAsync(Log, "{broken");
        var before = await File.ReadAllBytesAsync(Log);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Reset)));
        Assert.Contains("Line 2", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(Log));
    }

    [Fact]
    public async Task CancelledAppend_PreservesHistory()
    {
        await Initialize();
        var before = await File.ReadAllBytesAsync(Log);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Reset), cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(Log));
    }

    [Fact]
    public async Task ConcurrentDuplicateAppend_RecordsExactlyOneEvent()
    {
        await Initialize();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { await Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Reset)); return true; }
            catch (Exception exception) when (exception is IOException or InvalidOperationException) { return false; }
        }));
        Assert.Single(attempts, success => success);
        Assert.Equal(2, (await Store.ReadAllAsync()).Count);
    }

    [Fact]
    public async Task ExclusiveWriterLock_PreventsConcurrentReadOrAppend()
    {
        await Initialize();
        using (var stream = new FileStream(Log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => Store.ReadAllAsync());
            await Assert.ThrowsAsync<IOException>(() => Store.AppendAsync(Event("second", "b", "a", BaselineEventType.Reset)));
        }
        Assert.Single(await Store.ReadAllAsync());
    }

    [Fact]
    public async Task LegacyPointer_IsValidatedAndCanStartWithReset()
    {
        await Initialize();
        File.Delete(Log);
        var baselines = new FileBaselineStore(_root);
        await baselines.SetAsync(BaselineKind.Current, "a");
        await Store.AppendAsync(Event("migration", "b", "a", BaselineEventType.Reset));
        Assert.Equal("b", (await baselines.GetAsync(BaselineKind.Current))?.RunId);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"runId\":\"../escape\"}")]
    public async Task MalformedLegacyPointer_FailsClearly(string json)
    {
        Directory.CreateDirectory(Path.Combine(_root, "baselines"));
        await File.WriteAllTextAsync(Path.Combine(_root, "baselines", "current.json"), json);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new FileBaselineStore(_root).GetAsync(BaselineKind.Current));
        Assert.Contains("current.json", error.Message);
    }

    [Fact]
    public async Task InvalidKind_IsNotTreatedAsCurrent()
    {
        await Initialize();
        var baselines = new FileBaselineStore(_root);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => baselines.SetAsync((BaselineKind)42, "a"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => baselines.GetAsync((BaselineKind)42));
    }

    [Fact]
    public async Task Resolver_RejectsUnknownPreviousRunEvenForFirstLegacyEvent()
    {
        await Initialize();
        var runs = await new FileRunArchive(_root).ListAsync();
        var error = Assert.Throws<InvalidOperationException>(() => new BaselineResolver().Resolve(new("1.0", runs,
            [Event("legacy", "a", "missing", BaselineEventType.Reset)])));
        Assert.Contains("unknown previous run", error.Message);
    }

    private static string Serialize(BaselineEvent item) => JsonSerializer.Serialize(item,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n";

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
