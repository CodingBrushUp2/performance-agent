using System.Text.Json;

namespace PerformanceAgent.Core.History;

public sealed class FileRunArchive
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _archiveDirectory;

    public FileRunArchive(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _archiveDirectory = Path.Combine(Path.GetFullPath(rootDirectory), "archive");
    }

    public async Task<string> AppendAsync(
        ArchivedBenchmarkRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrWhiteSpace(run.RunId))
            throw new ArgumentException("Archived benchmark run must have a run ID.", nameof(run));

        Directory.CreateDirectory(_archiveDirectory);
        var path = Path.Combine(_archiveDirectory, $"{run.RunId}.json");
        if (File.Exists(path))
            throw new InvalidOperationException($"Archived benchmark run '{run.RunId}' already exists.");

        var json = JsonSerializer.Serialize(run, Options);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public async Task<ArchivedBenchmarkRun> ReadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (Path.GetFileName(runId) != runId)
            throw new ArgumentException("Run ID must not contain path segments.", nameof(runId));

        var path = Path.Combine(_archiveDirectory, $"{runId}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Archived benchmark run '{runId}' was not found.", path);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<ArchivedBenchmarkRun>(json, Options)
            ?? throw new InvalidOperationException($"Archived benchmark run '{runId}' is invalid.");
    }
}
