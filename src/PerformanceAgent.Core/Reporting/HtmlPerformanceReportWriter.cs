using System.Globalization;
using System.Net;
using System.Text;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.History;

namespace PerformanceAgent.Core.Reporting;

public sealed class HtmlPerformanceReportWriter
{
    public string Write(PerformanceReport report, PerformanceReportContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new StringBuilder();
        foreach (var comparison in report.Comparisons)
        {
            AppendMetric(rows, comparison.BenchmarkName, "Mean (ns)", comparison.Mean);
            AppendMetric(rows, comparison.BenchmarkName, "Allocation (B/op)", comparison.AllocatedBytes);
        }
        var interpretation = context is null
            ? "<p>No budget check or environment validation was requested for this numeric comparison.</p>"
            : $"<h2>{(context.Passed ? "PASS" : "REGRESSION")}</h2><p>Deterministic interpretation from the shared regression check, not AI analysis.</p><p>Budget source: {Encode(context.BudgetSource)}</p><p>Maximum mean regression: {Threshold(context.Budget.MaxMeanRegressionPercent)}; maximum allocation regression: {Threshold(context.Budget.MaxAllocationRegressionPercent)}.</p><p>Environment validation: compatible. Benchmark identities: matching.</p>";
        return $$"""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<title>Performance Agent report</title><style>
body{font:16px system-ui;color:#18212f;background:#fff;margin:2rem auto;padding:0 1rem;max-width:1100px}
table{border-collapse:collapse;width:100%}th,td{padding:.6rem;border:1px solid #bbb;text-align:left;overflow-wrap:anywhere}
th{background:#f1f3f5}dt{font-weight:600}dd{margin:0 0 .5rem;overflow-wrap:anywhere}section{margin:1.5rem 0}
@media print{body{max-width:none;margin:0}tr{break-inside:avoid} }
</style></head><body><h1>Performance Agent report</h1>
<p>Report schema: {{Encode(report.SchemaVersion)}}. Generated: {{report.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}}</p>
<section><h2>Interpretation</h2>{{interpretation}}
<p>PASS means no comparable metric exceeded its configured threshold. Unavailable metrics and percentages with a zero baseline are not treated as regressions by the existing checker; they are not evidence of equivalence.</p></section>
{{(context is null ? "" : Run("Baseline run", context.Baseline) + Run("Candidate run", context.Candidate))}}
<section><h2>Measured evidence and derived changes</h2><p>Baseline and candidate values are measured evidence. Percentage changes are derived calculations. Unavailable is never a measured zero.</p>
<table><thead><tr><th>Benchmark</th><th>Metric</th><th>Measured baseline</th><th>Measured candidate</th><th>Derived change (%)</th><th>Comparison status</th></tr></thead><tbody>{{rows}}</tbody></table></section>
<p>This self-contained report needs no server, scripts, network access or external assets. Archived evidence and baseline history were not modified.</p>
</body></html>
""";
    }

    private static void AppendMetric(StringBuilder rows, string name, string metric, MetricChange change) =>
        rows.Append($"<tr><td>{Encode(name)}</td><td>{metric}</td><td>{Number(change.Baseline)}</td><td>{Number(change.Candidate)}</td><td>{Number(change.PercentChange)}</td><td>{change.Status}</td></tr>");

    private static string Run(string title, ArchivedBenchmarkRun run) =>
        $"<section><h2>{title}</h2><dl><dt>RunId</dt><dd>{Encode(run.RunId)}</dd><dt>Timestamp</dt><dd>{run.Timestamp.ToString("O", CultureInfo.InvariantCulture)}</dd><dt>Commit SHA</dt><dd>{Encode(run.CommitSha ?? "Unavailable")}</dd><dt>Runtime</dt><dd>{Encode(run.Evidence.Environment?.Runtime ?? "Unavailable")}</dd><dt>OS</dt><dd>{Encode(run.Evidence.Environment?.OperatingSystem ?? "Unavailable")}</dd><dt>Architecture</dt><dd>{Encode(run.Evidence.Environment?.Architecture ?? "Unavailable")}</dd></dl></section>";

    private static string Number(double? value) => value?.ToString("G17", CultureInfo.InvariantCulture) ?? "Unavailable";
    private static string Threshold(double? value) => value is null ? "Not configured" : $"+{Number(value)}%";
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
