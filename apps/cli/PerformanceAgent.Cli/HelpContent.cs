using System.Net;
using System.Text;

namespace PerformanceAgent.Cli;

internal sealed record CommandHelp(
    string Name,
    string Summary,
    string Usage,
    string[] Examples,
    string? Notes = null);

internal static class HelpContent
{
    private static readonly CommandHelp[] Commands =
    [
        new("run", "Run BenchmarkDotNet benchmarks and archive measured evidence.",
            "perfagent run <benchmark.csproj> [--output <evidence.json>]",
            ["perfagent run MyBenchmarks.csproj", "perfagent run MyBenchmarks.csproj --output evidence.json"]),
        new("history", "View archived runs, baseline history, or one archived run.",
            "perfagent history [<run-id>]",
            ["perfagent history", "perfagent history run-20261005T112147186Z-a0fa5a18af991433"]),
        new("baseline", "Select the Current or Anchor baseline.",
            "perfagent baseline <set|anchor> <run-id>",
            ["perfagent baseline set <run-id>", "perfagent baseline anchor <run-id>"],
            "Current is the normal regression baseline. Anchor is a longer-lived reference and never silently replaces Current."),
        new("check", "Check candidate evidence against a baseline and performance budget.",
            "perfagent check --candidate <candidate.json> [--budget <budget.json>] [--format <text|json>]\n" +
            "perfagent check -r|--run-id <baseline-run-id> --candidate <candidate.json> [--budget <budget.json>] [--format <text|json>]\n" +
            "perfagent check -b|--baseline <baseline.json> --candidate <candidate.json> [--budget <budget.json>] [--format <text|json>]\n" +
            "perfagent check <baseline.json> <candidate.json> <max-mean-regression-%> <max-allocation-regression-%> [--format <text|json>]",
            [
                "perfagent check --candidate candidate.json",
                "perfagent check --candidate candidate.json --format json",
                "perfagent check -r <baseline-run-id> --candidate candidate.json",
                "perfagent check -b baseline.json --candidate candidate.json --budget performance-budget.json --format json"
            ],
            "The candidate is an evidence JSON file. With --candidate and no explicit baseline, the active Current baseline is used; a distinct Anchor is also checked. -r/--run-id selects an archived baseline run, not an archived candidate. Mean verdicts use BenchmarkDotNet 99.9% confidence intervals; if uncertainty crosses the budget boundary, the result is INCONCLUSIVE. Legacy evidence without statistical confidence remains readable but cannot produce a trusted mean verdict. JSON output is a versioned machine-readable verdict contract; exit codes remain 0 PASS, 1 FAIL, 2 INCONCLUSIVE or command error."),
        new("analyze", "Analyze an archived candidate regression with optional AI assistance.",
            "perfagent analyze <candidate-run-id>",
            ["perfagent analyze <candidate-run-id>"],
            "The deterministic measured result is authoritative. AI output is advisory and cannot change PASS/REGRESSION."),
        new("report", "Generate a self-contained HTML report for an archived candidate.",
            "perfagent report <candidate-run-id> [--baseline <run-id>] [--budget <budget.json>] > report.html",
            ["perfagent report <candidate-run-id> > report.html"]),
        new("calibrate", "Run repeated benchmarks and assess run-to-run stability.",
            "perfagent calibrate <benchmark.csproj> [--runs <count>] [--max-spread <percent>]",
            ["perfagent calibrate MyBenchmarks.csproj", "perfagent calibrate MyBenchmarks.csproj --runs 5 --max-spread 3"]),
        new("config", "Show effective workspace, user-level AI, and budget configuration.",
            "perfagent config show",
            ["perfagent config show"]),
        new("storage", "Show the workspace state directory and writeability.",
            "perfagent storage",
            ["perfagent storage"]),
        new("ui", "Start the loopback-only Local Web UI.",
            "perfagent ui [--no-open]",
            ["perfagent", "perfagent ui", "perfagent ui --no-open"]),
        new("compare", "Compare two numeric measurements directly and write a report.",
            "perfagent compare <name> <baseline-ns> <candidate-ns> <baseline-bytes> <candidate-bytes> <json|markdown|html>",
            ["perfagent compare Sample.Work 100 106 64 80 markdown"])
    ];

    public static bool TryRender(string[] args, out string help)
    {
        help = string.Empty;
        if (args.Length == 1 && IsHelp(args[0]))
        {
            help = RenderGeneral();
            return true;
        }

        if (args.Length == 2 && string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            var command = Find(args[1]);
            if (command is null) return false;
            help = RenderCommand(command);
            return true;
        }

        if (args.Length == 2 && IsHelp(args[1]))
        {
            var command = Find(args[0]);
            if (command is null) return false;
            help = RenderCommand(command);
            return true;
        }

        return false;
    }

    public static string RenderGeneral()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Performance Agent");
        builder.AppendLine();
        builder.AppendLine("Local-first, evidence-driven performance engineering.");
        builder.AppendLine("AI proposes. Measurements decide.");
        builder.AppendLine();
        builder.AppendLine("Usage:");
        builder.AppendLine("  perfagent                    Start the Local Web UI");
        builder.AppendLine("  perfagent <command> [options]");
        builder.AppendLine();
        builder.AppendLine("Commands:");
        foreach (var command in Commands)
            builder.AppendLine($"  {command.Name,-11} {command.Summary}");
        builder.AppendLine();
        builder.AppendLine("Typical workflow:");
        builder.AppendLine("  run -> baseline -> change code -> run -> check -> analyze -> report");
        builder.AppendLine();
        builder.AppendLine("Examples:");
        builder.AppendLine("  perfagent run MyBenchmarks.csproj");
        builder.AppendLine("  perfagent history");
        builder.AppendLine("  perfagent baseline set <run-id>");
        builder.AppendLine("  perfagent analyze <candidate-run-id>");
        builder.AppendLine("  perfagent report <candidate-run-id> > report.html");
        builder.AppendLine();
        builder.AppendLine("Use 'perfagent <command> --help' for command-specific help.");
        return builder.ToString();
    }

    public static string RenderWebHelp()
    {
        var commandRows = string.Join("", Commands.Select(command =>
            $"<tr><td><code>{Html(command.Name)}</code></td><td>{Html(command.Summary)}</td></tr>"));

        var details = string.Join("", Commands.Select(command =>
        {
            var examples = string.Join("", command.Examples.Select(example => $"<li><code>{Html(example)}</code></li>"));
            var notes = command.Notes is null ? "" : $"<p class=\"muted\">{Html(command.Notes)}</p>";
            return $"<section class=\"card help-command\"><h2>{Html(command.Name)}</h2><p>{Html(command.Summary)}</p><pre><code>{Html(command.Usage)}</code></pre><h3>Examples</h3><ul>{examples}</ul>{notes}</section>";
        }));

        return $$"""
<p><a href="/">Back to dashboard</a></p>
<h1>Help / Getting Started</h1>
<p class="muted">Performance Agent is local-first and evidence-driven. <strong>AI proposes. Measurements decide.</strong></p>
{{RenderGettingStartedHtml()}}
<h2>Command reference</h2>
<table><thead><tr><th>Command</th><th>Purpose</th></tr></thead><tbody>{{commandRows}}</tbody></table>
<div class="help-grid">{{details}}</div>
""";
    }

    public static string RenderGettingStartedHtml() => """
<section class="card getting-started">
<h2>Getting started</h2>
<ol>
<li><strong>Run benchmarks</strong><br><code>perfagent run &lt;project.csproj&gt;</code></li>
<li><strong>Review history</strong><br><code>perfagent history</code></li>
<li><strong>Select a trusted Current baseline</strong><br><code>perfagent baseline set &lt;run-id&gt;</code></li>
<li><strong>Change or optimize code, then benchmark again</strong><br><code>perfagent run &lt;project.csproj&gt;</code></li>
<li><strong>Check measured evidence</strong><br><code>perfagent check --candidate &lt;candidate.json&gt;</code></li>
<li><strong>Analyze an archived candidate</strong><br><code>perfagent analyze &lt;candidate-run-id&gt;</code></li>
<li><strong>Generate a portable report</strong><br><code>perfagent report &lt;candidate-run-id&gt; &gt; report.html</code></li>
</ol>
<p><strong>Current</strong> is the normal regression baseline. <strong>Anchor</strong> is a longer-lived reference.</p>
<p><strong>AI proposes. Measurements decide.</strong></p>
<p><strong>Deterministic results are authoritative.</strong> AI analysis is optional, advisory, and never changes measured benchmark results.</p>
<p><a href="/help">Open full command help</a></p>
</section>
""";

    private static string RenderCommand(CommandHelp command)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Performance Agent - {command.Name}");
        builder.AppendLine();
        builder.AppendLine(command.Summary);
        builder.AppendLine();
        builder.AppendLine("Usage:");
        foreach (var line in command.Usage.Split('\n'))
            builder.AppendLine($"  {line}");
        if (command.Examples.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Examples:");
            foreach (var example in command.Examples)
                builder.AppendLine($"  {example}");
        }
        if (command.Notes is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Notes:");
            builder.AppendLine($"  {command.Notes}");
        }
        return builder.ToString();
    }

    private static CommandHelp? Find(string name) =>
        Commands.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsHelp(string value) =>
        value is "--help" or "-h" || string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
