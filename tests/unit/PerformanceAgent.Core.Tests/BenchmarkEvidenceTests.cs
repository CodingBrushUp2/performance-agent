using System.Text.Json;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class BenchmarkEvidenceTests
{
    [Fact]
    public void JsonWriter_PreservesUnavailableAllocationAsNull()
    {
        var evidence = new BenchmarkEvidence(
            "1.0",
            [new BenchmarkMeasurement("Sum", 12.5, null)]);

        var json = new JsonBenchmarkEvidenceWriter().Write(evidence);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        var measurement = root.GetProperty("measurements")[0];
        Assert.Equal("Sum", measurement.GetProperty("name").GetString());
        Assert.Equal(12.5, measurement.GetProperty("meanNanoseconds").GetDouble());
        Assert.Equal(JsonValueKind.Null, measurement.GetProperty("allocatedBytesPerOperation").ValueKind);
    }
    [Fact]
    public void JsonReader_RejectsUnsupportedSchema()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new JsonBenchmarkEvidenceReader().Read("""{"schemaVersion":"2.0","measurements":[{"name":"Sum","meanNanoseconds":1,"allocatedBytesPerOperation":0}]}"""));

        Assert.Contains("Unsupported", exception.Message);
    }

    [Fact]
    public void JsonReader_RejectsMalformedJson()
    {
        Assert.Throws<InvalidOperationException>(
            () => new JsonBenchmarkEvidenceReader().Read("{not-json}"));
    }

    [Fact]
    public void JsonReader_RejectsInvalidMeasurement()
    {
        Assert.Throws<InvalidOperationException>(
            () => new JsonBenchmarkEvidenceReader().Read("""{"schemaVersion":"1.0","measurements":[{"name":"","meanNanoseconds":-1,"allocatedBytesPerOperation":-1}]}"""));
    }
}
