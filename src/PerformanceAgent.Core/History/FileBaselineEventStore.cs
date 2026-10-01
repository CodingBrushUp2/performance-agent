using System.Text;
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
        cancellationToken.ThrowIfCancellationRequested();
        BaselineEventValidation.Validate([baselineEvent]);
        await ValidateReferencesAsync([baselineEvent], cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Serialize cooperating writers and keep readers away from an incomplete append.
        await using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var events = Parse(text);
        BaselineEventValidation.Validate([.. events, baselineEvent]);
        await ValidateReferencesAsync(events, cancellationToken);
        var separator = text.Length != 0 && !text.EndsWith('\n') ? "\n" : string.Empty;
        var bytes = Encoding.UTF8.GetBytes(separator + JsonSerializer.Serialize(baselineEvent, Options) + "\n");
        var originalLength = stream.Length;
        stream.Position = originalLength;
        try
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch
        {
            // Roll back only this failed append; existing records remain byte-for-byte intact.
            stream.SetLength(originalLength);
            stream.Flush();
            throw;
        }
    }

    public async Task<IReadOnlyList<BaselineEvent>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
            return [];

        return Parse(await File.ReadAllTextAsync(_path, cancellationToken));
    }

    private async Task ValidateReferencesAsync(IReadOnlyList<BaselineEvent> events, CancellationToken cancellationToken)
    {
        var archive = new FileRunArchive(Path.GetDirectoryName(_path)!);
        var runIds = events.SelectMany(item => item.PreviousRunId is null
            ? new[] { item.RunId } : new[] { item.RunId, item.PreviousRunId });
        foreach (var runId in runIds.Distinct(StringComparer.Ordinal))
            _ = await archive.ReadAsync(runId, cancellationToken);
    }

    private static IReadOnlyList<BaselineEvent> Parse(string text)
    {
        var events = new List<BaselineEvent>();
        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                events.Add(JsonSerializer.Deserialize<BaselineEvent>(line, Options)
                    ?? throw new InvalidOperationException($"Baseline event history contains invalid JSON at line {lineNumber}."));
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"Baseline event history contains invalid JSON. Line {lineNumber}: {exception.Message}", exception);
            }
        }
        BaselineEventValidation.Validate(events);
        return events;
    }
}
