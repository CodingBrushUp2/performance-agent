using System.Text.Json;

namespace PerformanceAgent.Core.History;

public sealed record BaselineReference(string RunId);

public sealed class FileBaselineStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _rootDirectory;

    public FileBaselineStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task SetAsync(
        BaselineKind kind,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var archive = new FileRunArchive(_rootDirectory);
        _ = await archive.ReadAsync(runId, cancellationToken);

        var directory = Path.Combine(_rootDirectory, "baselines");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, kind == BaselineKind.Anchor ? "anchor.json" : "current.json");
        var json = JsonSerializer.Serialize(new BaselineReference(runId), Options);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<BaselineReference?> GetAsync(
        BaselineKind kind,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            _rootDirectory,
            "baselines",
            kind == BaselineKind.Anchor ? "anchor.json" : "current.json");

        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<BaselineReference>(json, Options)
            ?? throw new InvalidOperationException($"Baseline reference '{path}' is invalid.");
    }
}
