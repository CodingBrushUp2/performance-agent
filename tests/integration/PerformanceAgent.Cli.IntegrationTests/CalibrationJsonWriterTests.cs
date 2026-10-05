using System.Text.Json;
using PerformanceAgent.Core.Calibration;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class CalibrationJsonWriterTests
{
    [Fact]
    public void WritesStableContract()
    {
        var calibration = new CalibrationResult(
            true,
            5,
            [new CalibrationMetric("Sample.Work", 101, 2, 64, 0)],
            []);

        var json = new CalibrationJsonWriter().Write(
            calibration,
            ["run-1", "run-2", "run-3"]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("stable", root.GetProperty("status").GetString());
        Assert.Equal(3, root.GetProperty("runCount").GetInt32());
        Assert.Equal(3, root.GetProperty("runIds").GetArrayLength());
        Assert.Equal(5, root.GetProperty("maxAllowedSpreadPercent").GetDouble());

        var metric = Assert.Single(root.GetProperty("metrics").EnumerateArray());
        Assert.Equal("Sample.Work", metric.GetProperty("benchmarkName").GetString());
        Assert.Equal(101, metric.GetProperty("medianMeanNanoseconds").GetDouble());
        Assert.Equal(2, metric.GetProperty("meanSpreadPercent").GetDouble());
        Assert.Equal(64, metric.GetProperty("medianAllocatedBytesPerOperation").GetInt64());
        Assert.Equal(0, metric.GetProperty("allocationSpreadPercent").GetDouble());
        Assert.Empty(root.GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public void WritesUnstableReasons()
    {
        var calibration = new CalibrationResult(
            false,
            3,
            [new CalibrationMetric("Sample.Work", 100, 8, null, null)],
            ["Sample.Work: mean spread 8% exceeds 3%."]);

        var json = new CalibrationJsonWriter().Write(
            calibration,
            ["run-a", "run-b"]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("unstable", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("runCount").GetInt32());
        Assert.Single(root.GetProperty("reasons").EnumerateArray());
        var metric = Assert.Single(root.GetProperty("metrics").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, metric.GetProperty("medianAllocatedBytesPerOperation").ValueKind);
        Assert.Equal(JsonValueKind.Null, metric.GetProperty("allocationSpreadPercent").ValueKind);
    }
}
