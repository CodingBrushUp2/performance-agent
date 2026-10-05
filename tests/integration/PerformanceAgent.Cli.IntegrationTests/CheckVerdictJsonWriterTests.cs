using System.Text.Json;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Quality;
using PerformanceAgent.Core.Verdicts;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class CheckVerdictJsonWriterTests
{
    private static readonly BenchmarkEnvironment Environment =
        new(".NET 10", "Linux", "X64");

    [Fact]
    public void Writes_versioned_fail_contract_with_measured_evidence()
    {
        var check = new RegressionCheckService().Check(
            Evidence(100, 64),
            Evidence(110, 80),
            new PerformanceBudget(5, 10));

        var json = new CheckVerdictJsonWriter().Write(
            "candidate.json",
            new PerformanceBudget(5, 10),
            check.Verdict,
            [
                new CheckVerdictInput(
                    new CheckVerdictReference("runId", "run-baseline"),
                    check)
            ]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("fail", root.GetProperty("verdict").GetString());
        Assert.Equal("candidate.json", root.GetProperty("candidate").GetString());
        Assert.Equal(5, root.GetProperty("budget").GetProperty("maxMeanRegressionPercent").GetDouble());
        Assert.Equal(10, root.GetProperty("budget").GetProperty("maxAllocationRegressionPercent").GetDouble());

        var checkJson = Assert.Single(root.GetProperty("checks").EnumerateArray());
        Assert.Equal("runId", checkJson.GetProperty("reference").GetProperty("kind").GetString());
        Assert.Equal("run-baseline", checkJson.GetProperty("reference").GetProperty("id").GetString());
        Assert.Equal("fail", checkJson.GetProperty("verdict").GetString());

        var benchmark = Assert.Single(checkJson.GetProperty("benchmarks").EnumerateArray());
        Assert.Equal("Sample.Work", benchmark.GetProperty("name").GetString());
        Assert.Equal("fail", benchmark.GetProperty("verdict").GetString());

        var meanDecision = benchmark.GetProperty("meanDecision");
        Assert.Equal("conclusiveExceededBudget", meanDecision.GetProperty("status").GetString());
        Assert.True(meanDecision.GetProperty("minimumRegressionPercent").GetDouble() > 5);
        Assert.True(meanDecision.GetProperty("maximumRegressionPercent").GetDouble() > 5);
        Assert.Equal(99.9, meanDecision.GetProperty("confidenceLevelPercent").GetDouble());
        Assert.Equal(JsonValueKind.Null, meanDecision.GetProperty("reason").ValueKind);

        var mean = benchmark.GetProperty("mean");
        Assert.Equal(100, mean.GetProperty("baseline").GetDouble());
        Assert.Equal(110, mean.GetProperty("candidate").GetDouble());
        Assert.Equal(10, mean.GetProperty("percentChange").GetDouble());
        Assert.Equal("comparable", mean.GetProperty("status").GetString());
        Assert.True(mean.GetProperty("budgetExceeded").GetBoolean());

        var allocation = benchmark.GetProperty("allocation");
        Assert.Equal(64, allocation.GetProperty("baseline").GetDouble());
        Assert.Equal(80, allocation.GetProperty("candidate").GetDouble());
        Assert.Equal(25, allocation.GetProperty("percentChange").GetDouble());
        Assert.Equal("comparable", allocation.GetProperty("status").GetString());
        Assert.True(allocation.GetProperty("budgetExceeded").GetBoolean());
    }

    [Fact]
    public void Keeps_missing_metric_as_explicit_null_in_inconclusive_contract()
    {
        var check = new RegressionCheckService().Check(
            Evidence(100, 64),
            Evidence(101, null),
            new PerformanceBudget(5, 10));

        var json = new CheckVerdictJsonWriter().Write(
            "candidate.json",
            new PerformanceBudget(5, 10),
            check.Verdict,
            [
                new CheckVerdictInput(
                    new CheckVerdictReference("explicit", "baseline.json"),
                    check)
            ]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("inconclusive", root.GetProperty("verdict").GetString());
        Assert.NotEmpty(root.GetProperty("reasons").EnumerateArray());

        var benchmark = Assert.Single(
            Assert.Single(root.GetProperty("checks").EnumerateArray())
                .GetProperty("benchmarks")
                .EnumerateArray());

        var allocation = benchmark.GetProperty("allocation");
        Assert.Equal(JsonValueKind.Null, allocation.GetProperty("candidate").ValueKind);
        Assert.Equal(JsonValueKind.Null, allocation.GetProperty("percentChange").ValueKind);
        Assert.Equal("unavailable", allocation.GetProperty("status").GetString());
        Assert.False(allocation.GetProperty("budgetExceeded").GetBoolean());
    }

    [Fact]
    public void Preserves_multiple_reference_checks()
    {
        var current = new RegressionCheckService().Check(
            Evidence(100, 64),
            Evidence(101, 64),
            new PerformanceBudget(5, 10));
        var anchor = new RegressionCheckService().Check(
            Evidence(90, 64),
            Evidence(101, 64),
            new PerformanceBudget(5, 10));

        var json = new CheckVerdictJsonWriter().Write(
            "candidate.json",
            new PerformanceBudget(5, 10),
            PerformanceVerdict.Fail,
            [
                new CheckVerdictInput(new CheckVerdictReference("current", "run-current"), current),
                new CheckVerdictInput(new CheckVerdictReference("anchor", "run-anchor"), anchor)
            ]);

        using var document = JsonDocument.Parse(json);
        var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();

        Assert.Equal(2, checks.Length);
        Assert.Equal("current", checks[0].GetProperty("reference").GetProperty("kind").GetString());
        Assert.Equal("anchor", checks[1].GetProperty("reference").GetProperty("kind").GetString());
        Assert.Equal("fail", document.RootElement.GetProperty("verdict").GetString());
    }

    private static BenchmarkEvidence Evidence(double meanNanoseconds, long? allocatedBytes) =>
        new(
            "1.0",
            [
                new BenchmarkMeasurement(
                    "Sample.Work",
                    meanNanoseconds,
                    allocatedBytes,
                    new BenchmarkStatistics(
                        15,
                        meanNanoseconds,
                        0.1,
                        0.03,
                        0,
                        Math.Max(0, meanNanoseconds - 0.1),
                        meanNanoseconds + 0.1))
            ],
            Environment);
}
