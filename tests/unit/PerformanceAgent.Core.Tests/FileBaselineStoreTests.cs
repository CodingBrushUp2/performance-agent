using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class FileBaselineStoreTests
{
    [Fact]
    public async Task SetAndGet_CurrentBaseline_ReferencesArchivedRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var evidence = new BenchmarkEvidence("1.0", [new BenchmarkMeasurement("A", 100, 0)]);
            var archive = new FileRunArchive(root);
            await archive.AppendAsync(new ArchivedBenchmarkRun("run-1", DateTimeOffset.UtcNow, null, evidence));

            var store = new FileBaselineStore(root);
            await store.SetAsync(BaselineKind.Current, "run-1");

            var baseline = await store.GetAsync(BaselineKind.Current);
            Assert.NotNull(baseline);
            Assert.Equal("run-1", baseline.RunId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Set_RejectsUnknownRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileBaselineStore(root);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => store.SetAsync(BaselineKind.Anchor, "missing-run"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
