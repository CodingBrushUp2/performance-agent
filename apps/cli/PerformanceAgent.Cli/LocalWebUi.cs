using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PerformanceAgent.Core.History;

namespace PerformanceAgent.Cli;

internal static class LocalWebUi
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
        var archive = new FileRunArchive(root);
        var baselines = new FileBaselineStore(root);
        var runs = await archive.ListAsync(cancellationToken);
        var current = await baselines.GetAsync(BaselineKind.Current, cancellationToken);
        var anchor = await baselines.GetAsync(BaselineKind.Anchor, cancellationToken);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/", () => Results.Content(Render(runs, current?.RunId, anchor?.RunId), "text/html; charset=utf-8"));
        await app.StartAsync(cancellationToken);

        var address = app.Urls.First();
        Console.WriteLine($"Performance Agent UI: {address}");
        TryOpenBrowser(address);
        await app.WaitForShutdownAsync(cancellationToken);
        return 0;
    }

    private static string Render(IReadOnlyList<ArchivedBenchmarkRun> runs, string? current, string? anchor)
    {
        var rows = string.Join("", runs.OrderByDescending(x => x.Timestamp).Select(run =>
            $"<tr><td><code>{WebUtility.HtmlEncode(run.RunId)}</code></td><td>{run.Timestamp:O}</td><td>{Label(run.RunId, current, anchor)}</td></tr>"));
        if (rows.Length == 0) rows = "<tr><td colspan=\"3\">No benchmark runs yet.</td></tr>";
        return $$"""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Performance Agent</title><style>
body{font:15px system-ui;margin:0;background:#f6f7f9;color:#1f2937}main{max-width:1000px;margin:48px auto;padding:0 24px}
h1{font-size:28px}.cards{display:grid;grid-template-columns:repeat(2,1fr);gap:16px;margin:24px 0}
.card,table{background:white;border:1px solid #e5e7eb;border-radius:10px}.card{padding:18px}.muted{color:#6b7280}
table{width:100%;border-collapse:collapse;overflow:hidden}th,td{text-align:left;padding:13px;border-bottom:1px solid #eee}th{background:#fafafa}
.badge{display:inline-block;padding:3px 8px;border-radius:999px;background:#eef2ff;margin-right:5px}code{font-size:13px}
</style></head><body><main><h1>Performance Agent</h1><p class="muted">Local performance evidence. CLI remains the primary interface.</p>
<div class="cards"><div class="card"><div class="muted">Current baseline</div><strong>{{WebUtility.HtmlEncode(current ?? "Not set")}}</strong></div>
<div class="card"><div class="muted">Anchor baseline</div><strong>{{WebUtility.HtmlEncode(anchor ?? "Not set")}}</strong></div></div>
<h2>Benchmark history</h2><table><thead><tr><th>Run</th><th>Timestamp</th><th>Baseline</th></tr></thead><tbody>{{rows}}</tbody></table>
</main></body></html>
""";
    }

    private static string Label(string id, string? current, string? anchor)
    {
        var labels = new List<string>();
        if (id == current) labels.Add("<span class=\"badge\">Current</span>");
        if (id == anchor) labels.Add("<span class=\"badge\">Anchor</span>");
        return labels.Count == 0 ? "—" : string.Join("", labels);
    }

    private static void TryOpenBrowser(string address)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch { Console.WriteLine("Open the URL above in your browser."); }
    }
}
