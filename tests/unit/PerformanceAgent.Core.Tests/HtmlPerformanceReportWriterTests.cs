using System.Globalization;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Reporting;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class HtmlPerformanceReportWriterTests
{
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly ComparisonResult Comparison = new("<script>bench</script>",
        new MetricChange(100, 125.5, 25.5, ComparisonStatus.Comparable),
        new MetricChange(null, 0, null, ComparisonStatus.Unavailable));
    private static readonly PerformanceReport Report = new("1.0", Timestamp, [Comparison]);

    [Fact]
    public void NumericReport_IsSelfContainedAndDoesNotInventBudgetDecision()
    {
        var html = new HtmlPerformanceReportWriter().Write(Report);
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("&lt;script&gt;bench&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain(" src=", html);
        Assert.DoesNotContain(" href=", html);
        Assert.Contains("No budget check or environment validation was requested", html);
        Assert.Contains("<td>Unavailable</td><td>0</td><td>Unavailable</td>", html);
        Assert.DoesNotContain("<h2>PASS</h2>", html);
    }

    [Theory]
    [InlineData(true, "PASS")]
    [InlineData(false, "REGRESSION")]
    public void ContextRendersSharedCheckResultAndEncodedProvenance(bool passed, string status)
    {
        var evidence = new BenchmarkEvidence("1.0", [new BenchmarkMeasurement("A", 100, null)],
            new BenchmarkEnvironment("Runtime <unsafe>", "OS & test", "X64"));
        var baseline = new ArchivedBenchmarkRun("baseline", Timestamp, "sha<unsafe>", evidence);
        var candidate = baseline with { RunId = "candidate" };
        var context = new PerformanceReportContext(baseline, candidate, new PerformanceBudget(5, null), "budget<file>", passed);
        var html = new HtmlPerformanceReportWriter().Write(Report, context);
        Assert.Contains($"<h2>{status}</h2>", html);
        Assert.Contains("Runtime &lt;unsafe&gt;", html);
        Assert.Contains("sha&lt;unsafe&gt;", html);
        Assert.Contains("OS &amp; test", html);
        Assert.Contains("budget&lt;file&gt;", html);
        Assert.Contains("maximum allocation regression: Not configured", html);
        Assert.Contains("Environment validation: compatible", html);
        Assert.Contains("<dd>baseline</dd>", html);
        Assert.Contains("<dd>candidate</dd>", html);
    }

    [Fact]
    public void NumbersAreInvariantAndZeroBaselinePercentageRemainsUnavailable()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            var report = Report with { Comparisons = [Comparison with { AllocatedBytes = new(0, 64, null, ComparisonStatus.NoBaseline) }] };
            var html = new HtmlPerformanceReportWriter().Write(report);
            Assert.Contains("<td>125.5</td>", html);
            Assert.Contains("<td>0</td><td>64</td><td>Unavailable</td><td>NoBaseline</td>", html);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
