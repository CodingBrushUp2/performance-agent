using System.Text.Json;
using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Cli.IntegrationTests;

/// <summary>A real, file-backed Performance Agent workspace in a temporary directory.</summary>
internal sealed class TestWorkspace : IDisposable
{
    private static readonly BenchmarkEnvironment Environment = new(".NET 10.0.0", "Linux", "X64");

    public TestWorkspace()
    {
        Directory = Path.Combine(Path.GetTempPath(), $"perfagent-analyze-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Directory);
        Storage = WorkspaceStorage.Resolve(Directory, Path.Combine(Directory, "user-config.json"));
    }

    public string Directory { get; }
    public WorkspaceStorage Storage { get; }

    public async Task<BenchmarkEvidence> ArchiveAsync(string runId, double meanNanoseconds, long allocatedBytes, string benchmarkName = "Sample.Work")
    {
        var evidence = new BenchmarkEvidence("1.0", [new BenchmarkMeasurement(benchmarkName, meanNanoseconds, allocatedBytes)], Environment);
        await new FileRunArchive(Storage.StateDirectory).AppendAsync(
            new ArchivedBenchmarkRun(runId, DateTimeOffset.UtcNow, null, evidence));
        return evidence;
    }

    public Task SetBaselineAsync(BaselineKind kind, string runId) =>
        new BaselineSelectionService(Storage).SetAsync(kind, runId, "test setup");

    public void WriteConfiguration(object configuration) =>
        File.WriteAllText(Path.Combine(Directory, "perfagent.json"), JsonSerializer.Serialize(configuration));

    public void WriteUserConfiguration(object configuration) =>
        File.WriteAllText(Storage.UserConfigurationPath!, JsonSerializer.Serialize(configuration));

    /// <summary>Every file under the state directory with its exact bytes, to prove analysis writes nothing.</summary>
    public IReadOnlyDictionary<string, string> SnapshotState() =>
        System.IO.Directory.EnumerateFiles(Storage.StateDirectory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(Storage.StateDirectory, path),
                path => Convert.ToBase64String(File.ReadAllBytes(path)));

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>Offline stand-in for a configured provider; records what analysis receives. Never calls a model.</summary>
internal sealed class RecordingProvider(
    Func<PerformanceAnalysisRequest, CancellationToken, Task<PerformanceAnalysis>> analyze) : IPerformanceAnalysisProvider, IDisposable
{
    public List<PerformanceAnalysisRequest> Requests { get; } = [];
    public bool Disposed { get; private set; }

    public static RecordingProvider Returning(PerformanceAnalysis analysis) => new((_, _) => Task.FromResult(analysis));

    public Task<PerformanceAnalysis> AnalyzeAsync(PerformanceAnalysisRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return analyze(request, cancellationToken);
    }

    public void Dispose() => Disposed = true;
}

internal static class Analyses
{
    public static PerformanceAnalysis Claiming(string summary) => new(
        summary,
        [new("Sample.Work", "Mean moved between runs.")],
        [new("A cache may be cold.", "Timing changed.")],
        [new("Re-run with a warmed cache.", "Mean returns to baseline.")],
        "No source code was supplied.");
}
