using System.Text.Json;

namespace PerformanceAgent.Core.History;

public sealed class FileBaselineEventStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileBaselineEventStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _path = Path.Combine(Path.GetFullPath(rootDirectory), "baseline-events.jsonl");
    }

    public async Task AppendAsync(
        BaselineEvent baselineEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baselineEvent);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var line = JsonSerializer.Serialize(baselineEvent, Options) + Environment.NewLine;
        await File.AppendAllTextAsync(_path, line, cancellationToken);
    }

    public async Task<IReadOnlyList<BaselineEvent>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return [];

        var events = new List<BaselineEvent>();
        foreach (var line in await File.ReadAllLinesAsync(_path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var item = JsonSerializer.Deserialize<BaselineEvent>(line, Options)
                    ?? throw new InvalidOperationException("Baseline event history contains invalid JSON.");
                if (string.IsNullOrWhiteSpace(item.EventId) || string.IsNullOrWhiteSpace(item.RunId)
                    || !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Type))
                    throw new InvalidOperationException("Baseline event history contains an invalid event.");
                events.Add(item);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Baseline event history contains invalid JSON.", exception);
            }
        }

        return events;
    }
}
