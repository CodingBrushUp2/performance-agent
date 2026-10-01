using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Evidence;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class FileBaselineEventStoreTests
{
    [Fact]
    public async Task AppendAndReadAll_PreservesBaselineHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileBaselineEventStore(root);
            var archive = new FileRunArchive(root);
            foreach (var id in new[] { "run-1", "run-2" })
                await archive.AppendAsync(new ArchivedBenchmarkRun(id, DateTimeOffset.UtcNow, null, new BenchmarkEvidence("1.0", [])));
            var first = new BaselineEvent(
                "event-1",
                DateTimeOffset.Parse("2026-09-30T12:00:00Z"),
                BaselineKind.Current,
                BaselineEventType.Created,
                "run-1",
                null,
                "initial");
            var reset = new BaselineEvent(
                "event-2",
                DateTimeOffset.Parse("2026-09-30T13:00:00Z"),
                BaselineKind.Current,
                BaselineEventType.Reset,
                "run-2",
                "run-1",
                "reviewed reset");

            await store.AppendAsync(first);
            await store.AppendAsync(reset);

            var events = await store.ReadAllAsync();
            Assert.Equal(2, events.Count);
            Assert.Equal(first, events[0]);
            Assert.Equal(reset, events[1]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
