using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class BaselineResolverTests
{
    [Fact]
    public void Resolve_PreservesAnchorWhenCurrentBaselineIsReset()
    {
        var evidence = new BenchmarkEvidence(
            "1.0",
            [new BenchmarkMeasurement("A", 100, 0)],
            new BenchmarkEnvironment(".NET 10", "Linux", "X64"));

        var runs = new[]
        {
            new ArchivedBenchmarkRun("run-1", DateTimeOffset.Parse("2026-09-01T00:00:00Z"), "aaa", evidence),
            new ArchivedBenchmarkRun("run-2", DateTimeOffset.Parse("2026-09-10T00:00:00Z"), "bbb", evidence),
            new ArchivedBenchmarkRun("run-3", DateTimeOffset.Parse("2026-09-20T00:00:00Z"), "ccc", evidence)
        };

        var events = new[]
        {
            new BaselineEvent("event-1", DateTimeOffset.Parse("2026-09-01T00:01:00Z"), BaselineKind.Anchor, BaselineEventType.Created, "run-1", null, "Initial calibration"),
            new BaselineEvent("event-2", DateTimeOffset.Parse("2026-09-10T00:01:00Z"), BaselineKind.Current, BaselineEventType.Promoted, "run-2", null, "Accepted change"),
            new BaselineEvent("event-3", DateTimeOffset.Parse("2026-09-20T00:01:00Z"), BaselineKind.Current, BaselineEventType.Reset, "run-3", "run-2", "Reviewed reset")
        };

        var result = new BaselineResolver().Resolve(new BenchmarkHistory("1.0", runs, events));

        Assert.Equal("run-1", result.AnchorRunId);
        Assert.Equal("run-3", result.CurrentRunId);
    }

    [Fact]
    public void Resolve_RejectsBaselineEventForUnknownRun()
    {
        var history = new BenchmarkHistory(
            "1.0",
            [],
            [new BaselineEvent("event-1", DateTimeOffset.UtcNow, BaselineKind.Current, BaselineEventType.Reset, "missing", null, null)]);

        Assert.Throws<InvalidOperationException>(() => new BaselineResolver().Resolve(history));
    }
}
