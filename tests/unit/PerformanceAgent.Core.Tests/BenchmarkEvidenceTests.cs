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
    public void JsonWriterAndReader_PreserveOptionalStatistics()
    {
        var evidence = new BenchmarkEvidence(
            "1.0",
            [
                new BenchmarkMeasurement(
                    "Sum",
                    12.5,
                    64,
                    new BenchmarkStatistics(15, 12.1, 0.8, 0.2, 1, 11.8, 13.2, 99.9))
            ]);

        var json = new JsonBenchmarkEvidenceWriter().Write(evidence);
        var roundTrip = new JsonBenchmarkEvidenceReader().Read(json);

        var statistics = Assert.IsType<BenchmarkStatistics>(Assert.Single(roundTrip.Measurements).Statistics);
        Assert.Equal(15, statistics.SampleCount);
        Assert.Equal(12.1, statistics.MedianNanoseconds);
        Assert.Equal(0.8, statistics.StandardDeviationNanoseconds);
        Assert.Equal(0.2, statistics.StandardErrorNanoseconds);
        Assert.Equal(1, statistics.OutlierCount);
        Assert.Equal(11.8, statistics.ConfidenceIntervalLowerNanoseconds);
        Assert.Equal(13.2, statistics.ConfidenceIntervalUpperNanoseconds);
        Assert.Equal(99.9, statistics.ConfidenceLevelPercent);
    }

    [Fact]
    public void JsonReader_Rejects_partial_confidence_interval()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "measurements": [
            {
              "name": "Sum",
              "meanNanoseconds": 12.5,
              "allocatedBytesPerOperation": 64,
              "statistics": {
                "sampleCount": 15,
                "medianNanoseconds": 12.1,
                "standardDeviationNanoseconds": 0.8,
                "standardErrorNanoseconds": 0.2,
                "outlierCount": 1,
                "confidenceIntervalLowerNanoseconds": 11.8
              }
            }
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => new JsonBenchmarkEvidenceReader().Read(json));
    }

    [Fact]
    public void JsonReader_AcceptsLegacyEvidenceWithoutStatistics()
    {
        var evidence = new JsonBenchmarkEvidenceReader().Read(
            """{"schemaVersion":"1.0","measurements":[{"name":"Sum","meanNanoseconds":12.5,"allocatedBytesPerOperation":64}]}""");

        Assert.Null(Assert.Single(evidence.Measurements).Statistics);
    }

    [Fact]
    public void JsonReader_RejectsInvalidStatistics()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "measurements": [
            {
              "name": "Sum",
              "meanNanoseconds": 12.5,
              "allocatedBytesPerOperation": 64,
              "statistics": {
                "sampleCount": 0,
                "medianNanoseconds": 12.1,
                "standardDeviationNanoseconds": 0.8,
                "standardErrorNanoseconds": 0.2,
                "outlierCount": 1
              }
            }
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => new JsonBenchmarkEvidenceReader().Read(json));
    }

    [Fact]
    public void JsonReader_RejectsInvalidLogicalProcessorCount()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "environment": {
            "runtime": ".NET 10",
            "operatingSystem": "Linux",
            "architecture": "X64",
            "logicalProcessorCount": 0,
            "serverGarbageCollection": false
          },
          "measurements": [
            {
              "name": "Sum",
              "meanNanoseconds": 12.5,
              "allocatedBytesPerOperation": 64
            }
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => new JsonBenchmarkEvidenceReader().Read(json));
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


public sealed class BenchmarkEvidenceEnvironmentValidationTests
{
    [Fact]
    public void Reader_RejectsEmptyEnvironmentFields()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "measurements": [{ "name": "A", "meanNanoseconds": 1, "allocatedBytesPerOperation": 0 }],
          "environment": { "runtime": "", "operatingSystem": "Linux", "architecture": "X64" }
        }
        """;

        Assert.Throws<InvalidOperationException>(() => new JsonBenchmarkEvidenceReader().Read(json));
    }

    [Fact]
    public void Reader_RejectsDuplicateMeasurementIdentities()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "measurements": [
            { "name": "A", "meanNanoseconds": 1, "allocatedBytesPerOperation": 0 },
            { "name": "A", "meanNanoseconds": 2, "allocatedBytesPerOperation": 0 }
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => new JsonBenchmarkEvidenceReader().Read(json));
    }
}
