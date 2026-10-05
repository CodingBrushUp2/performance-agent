using System.Text.Json;

namespace PerformanceAgent.Cli;

internal sealed class CandidateJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Write(CandidateAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var document = new
        {
            schemaVersion = "1.0",
            baseRef = result.BaseRef,
            headRef = result.HeadRef,
            changedCSharpFileCount = result.ChangedCSharpFileCount,
            eligibleCSharpFileCount = result.EligibleCSharpFileCount,
            candidates = result.Candidates
        };

        return JsonSerializer.Serialize(document, Options);
    }
}
