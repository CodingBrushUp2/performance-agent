using PerformanceAgent.Core.Comparison;

namespace PerformanceAgent.Cli;

// Shared by `check` and `analyze` so measured results are presented identically.
internal static class CheckFormatting
{
    public static string FormatBudget(double? threshold, bool exceeded) =>
        threshold is null
            ? " (budget not configured)"
            : $" (budget +{threshold:0.##}%) {(exceeded ? "FAIL" : "PASS")}";

    public static string FormatChange(MetricChange change) =>
        change.Status == ComparisonStatus.Comparable && change.PercentChange is not null
            ? $"{change.Baseline:0.##} -> {change.Candidate:0.##} ({change.PercentChange:+0.##;-0.##;0}%)"
            : $"{change.Baseline?.ToString() ?? "n/a"} -> {change.Candidate?.ToString() ?? "n/a"} ({change.Status})";
}
