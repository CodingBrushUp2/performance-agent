using System.Text.Json;

namespace PerformanceAgent.Core.Evidence;

public sealed class JsonBenchmarkEvidenceWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Write(BenchmarkEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return JsonSerializer.Serialize(evidence, Options);
    }
}
