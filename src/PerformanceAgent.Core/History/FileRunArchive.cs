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
        RunIdValidation.Validate(run.RunId);

        Directory.CreateDirectory(_archiveDirectory);
        var path = Path.Combine(_archiveDirectory, $"{run.RunId}.json");
        if (File.Exists(path))
            throw new InvalidOperationException($"Archived benchmark run '{run.RunId}' already exists.");

        var json = JsonSerializer.Serialize(run, Options);
        var temporaryPath = Path.Combine(_archiveDirectory, $".{run.RunId}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            throw new InvalidOperationException($"Archived benchmark run '{run.RunId}' already exists.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return path;
    }

    public async Task<IReadOnlyList<ArchivedBenchmarkRun>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_archiveDirectory))
            return [];

        var runs = new List<ArchivedBenchmarkRun>();
        foreach (var path in Directory.EnumerateFiles(_archiveDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            runs.Add(await ReadFileAsync(path, Path.GetFileNameWithoutExtension(path), cancellationToken));
        }

        return runs
            .OrderByDescending(run => run.Timestamp)
            .ThenBy(run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ArchivedBenchmarkRun> ReadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        RunIdValidation.Validate(runId);

        var path = Path.Combine(_archiveDirectory, $"{runId}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Archived benchmark run '{runId}' was not found.", path);

        return await ReadFileAsync(path, runId, cancellationToken);
    }

    private static async Task<ArchivedBenchmarkRun> ReadFileAsync(
        string path, string expectedRunId, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var run = JsonSerializer.Deserialize<ArchivedBenchmarkRun>(json, Options)
                ?? throw new InvalidOperationException($"Archived benchmark run '{path}' is invalid.");
            RunIdValidation.Validate(run.RunId);
            if (!string.Equals(run.RunId, expectedRunId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Archived benchmark run '{path}' has RunId '{run.RunId}', expected '{expectedRunId}'.");
            return run;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"Archived benchmark run '{path}' contains invalid JSON or an invalid RunId.", exception);
        }
    }
}
