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
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
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
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        // Validate the entire referenced history, including superseded and previous runs.
        var events = await new FileBaselineEventStore(_rootDirectory).ReadAllAsync(cancellationToken);
        if (events.Count != 0)
        {
            var archive = new FileRunArchive(_rootDirectory);
            var runs = new List<ArchivedBenchmarkRun>();
            var runIds = events.SelectMany(item => item.PreviousRunId is null
                ? new[] { item.RunId } : new[] { item.RunId, item.PreviousRunId });
            foreach (var runId in runIds.Distinct(StringComparer.Ordinal))
                runs.Add(await archive.ReadAsync(runId, cancellationToken));
            var resolved = new BaselineResolver().Resolve(new BenchmarkHistory("1.0", runs, events));
            var selected = kind == BaselineKind.Anchor ? resolved.AnchorRunId : resolved.CurrentRunId;
            if (selected is not null)
                return new BaselineReference(selected);
        }

        // Preserve baselines created before event history was introduced.
        var path = Path.Combine(
            _rootDirectory,
            "baselines",
            kind == BaselineKind.Anchor ? "anchor.json" : "current.json");

        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var reference = JsonSerializer.Deserialize<BaselineReference>(json, Options)
                ?? throw new InvalidOperationException($"Baseline reference '{path}' is invalid.");
            _ = await new FileRunArchive(_rootDirectory).ReadAsync(reference.RunId, cancellationToken);
            return reference;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"Baseline reference '{path}' is invalid.", exception);
        }
    }
}
