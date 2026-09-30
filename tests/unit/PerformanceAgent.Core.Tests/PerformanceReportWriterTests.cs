using System.Text.Json;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Reporting;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class PerformanceReportWriterTests
{
    private static readonly PerformanceReport Report = new(
        "1.0",
        DateTimeOffset.Parse("2026-09-30T00:00:00Z"),
        [
            new ComparisonResult(
                "MapOrder",
                new MetricChange(100, 80, -20, ComparisonStatus.Comparable),
                new MetricChange(0, 32, null, ComparisonStatus.NoBaseline))
        ]);

    [Fact]
    public void JsonWriter_ProducesValidPortableReport()
    {
        var json = new JsonPerformanceReportWriter().Write(Report);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("mapOrder".ToLowerInvariant(), document.RootElement
            .GetProperty("comparisons")[0]
            .GetProperty("benchmarkName")
            .GetString()!
            .ToLowerInvariant());
        Assert.Equal("noBaseline", document.RootElement
            .GetProperty("comparisons")[0]
            .GetProperty("allocatedBytes")
            .GetProperty("status")
            .GetString());
    }

    [Fact]
    public void MarkdownWriter_SeparatesUnavailablePercentChange()
    {
        var markdown = new MarkdownPerformanceReportWriter().Write(Report);

        Assert.Contains("| MapOrder | Mean (ns) | 100 | 80 | -20% | Comparable |", markdown);
        Assert.Contains("| MapOrder | Allocated (B/op) | 0 | 32 | n/a | NoBaseline |", markdown);
    }
    [Fact]
    public void MarkdownWriter_RendersUnavailableMetricValuesAsNotAvailable()
    {
        var report = new PerformanceReport(
            "1.0",
            DateTimeOffset.Parse("2026-09-30T00:00:00Z"),
            [
                new ComparisonResult(
                    "MapOrder",
                    new MetricChange(100, 80, -20, ComparisonStatus.Comparable),
                    new MetricChange(null, 0, null, ComparisonStatus.Unavailable))
            ]);

        var markdown = new MarkdownPerformanceReportWriter().Write(report);

        Assert.Contains("| MapOrder | Allocated (B/op) | n/a | 0 | n/a | Unavailable |", markdown);
    }
}
