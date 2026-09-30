using System.Globalization;
using System.Text;
using PerformanceAgent.Core.Comparison;

namespace PerformanceAgent.Core.Reporting;

public sealed class MarkdownPerformanceReportWriter
{
    public string Write(PerformanceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("# Performance Report");
        builder.AppendLine();
        builder.AppendLine("| Benchmark | Metric | Baseline | Candidate | Change | Status |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");

        foreach (var comparison in report.Comparisons)
        {
            AppendMetric(builder, comparison.BenchmarkName, "Mean (ns)", comparison.Mean);
            AppendMetric(builder, comparison.BenchmarkName, "Allocated (B/op)", comparison.AllocatedBytes);
        }

        return builder.ToString();
    }

    private static void AppendMetric(
        StringBuilder builder,
        string benchmark,
        string metric,
        MetricChange change)
    {
        var percent = change.PercentChange is null
            ? "n/a"
            : $"{change.PercentChange.Value.ToString("0.##", CultureInfo.InvariantCulture)}%";

        builder.Append("| ")
            .Append(benchmark)
            .Append(" | ")
            .Append(metric)
            .Append(" | ")
            .Append(change.Baseline.ToString("0.##", CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(change.Candidate.ToString("0.##", CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(percent)
            .Append(" | ")
            .Append(change.Status)
            .AppendLine(" |");
    }
}
