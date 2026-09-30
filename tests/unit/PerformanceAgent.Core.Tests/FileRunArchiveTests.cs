using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class FileRunArchiveTests
{
    [Fact]
    public async Task AppendAndRead_PreservesRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var evidence = new BenchmarkEvidence(
                "1.0",
                [new BenchmarkMeasurement("A", 100, 0)],
                new BenchmarkEnvironment(".NET 10", "Linux", "X64"));
            var run = new ArchivedBenchmarkRun("run-test", DateTimeOffset.Parse("2026-09-30T12:00:00Z"), "abc123", evidence);
            var archive = new FileRunArchive(root);

            await archive.AppendAsync(run);
            var loaded = await archive.ReadAsync("run-test");

            Assert.Equal(run.RunId, loaded.RunId);
            Assert.Equal(run.Timestamp, loaded.Timestamp);
            Assert.Equal(run.CommitSha, loaded.CommitSha);
            Assert.Equal(run.Evidence.SchemaVersion, loaded.Evidence.SchemaVersion);
            Assert.Equal(run.Evidence.Environment, loaded.Evidence.Environment);
            Assert.Equal(run.Evidence.Measurements.ToArray(), loaded.Evidence.Measurements.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Append_RefusesToOverwriteExistingRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var evidence = new BenchmarkEvidence(
                "1.0",
                [new BenchmarkMeasurement("A", 100, 0)]);
            var run = new ArchivedBenchmarkRun("run-test", DateTimeOffset.UtcNow, null, evidence);
            var archive = new FileRunArchive(root);

            await archive.AppendAsync(run);

            await Assert.ThrowsAsync<InvalidOperationException>(() => archive.AppendAsync(run));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Read_RejectsPathTraversal()
    {
        var archive = new FileRunArchive(Path.GetTempPath());

        await Assert.ThrowsAsync<ArgumentException>(() => archive.ReadAsync("../secret"));
    }


    [Fact]
    public async Task List_ReturnsNewestRunFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var evidence = new BenchmarkEvidence("1.0", [new BenchmarkMeasurement("A", 100, 0)]);
            var archive = new FileRunArchive(root);
            await archive.AppendAsync(new ArchivedBenchmarkRun(
                "run-old",
                DateTimeOffset.Parse("2026-09-30T12:00:00Z"),
                null,
                evidence));
            await archive.AppendAsync(new ArchivedBenchmarkRun(
                "run-new",
                DateTimeOffset.Parse("2026-09-30T13:00:00Z"),
                null,
                evidence));

            var runs = await archive.ListAsync();

            Assert.Equal(["run-new", "run-old"], runs.Select(run => run.RunId).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task List_EmptyArchive_ReturnsEmptyCollection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var runs = await new FileRunArchive(root).ListAsync();

            Assert.Empty(runs);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RunIdGenerator_CreatesDistinctSortableIds()
    {
        var generator = new RunIdGenerator();
        var earlier = generator.Create(DateTimeOffset.Parse("2026-09-30T12:00:00Z"));
        var later = generator.Create(DateTimeOffset.Parse("2026-09-30T12:00:01Z"));

        Assert.NotEqual(earlier, later);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }
}
