using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Globalization;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Budgets;

namespace PerformanceAgent.Cli;

internal static class LocalWebUi
{
    public static async Task<int> RunAsync(bool openBrowser, CancellationToken cancellationToken)
    {
        var storage = WorkspaceStorage.Resolve();
        var archive = new FileRunArchive(storage.StateDirectory);
        var baselines = new FileBaselineStore(storage.StateDirectory);
        var selection = new BaselineSelectionService(storage);
        var configuration = new WorkspaceConfiguration(storage);

        var builder = WebApplication.CreateSlimBuilder();
        // The local UI must not inherit endpoint settings from a benchmark project's
        // appsettings.json or ASP.NET/Kestrel environment variables.
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxRequestBodySize = 16 * 1024;
        });
        // Tokens are scoped to this server lifetime; no key files or accounts are needed.
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAntiforgery(options => options.Cookie.SameSite = SameSiteMode.Strict);
        await using var app = builder.Build();
        var antiforgery = app.Services.GetRequiredService<IAntiforgery>();
        app.Use(async (context, next) =>
        {
            // Loopback binding alone does not protect against DNS rebinding.
            var expectedHost = $"127.0.0.1:{context.Connection.LocalPort}";
            if (!string.Equals(context.Request.Host.Value, expectedHost, StringComparison.Ordinal)
                || (context.Request.Headers.TryGetValue("Origin", out var origin)
                    && !string.Equals(origin.ToString(), $"http://{expectedHost}", StringComparison.Ordinal))
                || string.Equals(context.Request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Only same-origin loopback requests are allowed.");
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
            try { await next(context); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync($"Unable to complete the request: {exception.Message}\nReload history to check the active baseline before retrying. No administrator/root privileges are required.");
            }
        });
        app.MapGet("/configuration", () =>
        {
            var effective = configuration.Inspect();
            var content = $"<p><a href=\"/\">Back to history</a></p><h1>Effective configuration</h1><dl><dt>Configuration file</dt><dd>{WebUtility.HtmlEncode(effective.Path)}</dd><dt>Budget source</dt><dd>{WebUtility.HtmlEncode(effective.BudgetSource)}</dd><dt>Max mean regression (%)</dt><dd>{effective.Budget.MaxMeanRegressionPercent?.ToString(CultureInfo.InvariantCulture) ?? "Not configured"}</dd><dt>Max allocation regression (%)</dt><dd>{effective.Budget.MaxAllocationRegressionPercent?.ToString(CultureInfo.InvariantCulture) ?? "Not configured"}</dd></dl><p>Explicit CLI check thresholds or --budget override workspace settings. Do not store secrets in perfagent.json.</p>";
            return Results.Content(Page("Effective configuration — Performance Agent", content), "text/html; charset=utf-8");
        });
        app.MapGet("/", async (HttpContext context) =>
        {
            var runs = await archive.ListAsync(context.RequestAborted);
            var current = await baselines.GetAsync(BaselineKind.Current, context.RequestAborted);
            var anchor = await baselines.GetAsync(BaselineKind.Anchor, context.RequestAborted);
            var events = await new FileBaselineEventStore(storage.StateDirectory).ReadAllAsync(context.RequestAborted);
            var token = antiforgery.GetAndStoreTokens(context);
            return Results.Content(Render(runs, current?.RunId, anchor?.RunId, events, token, storage.Inspect()), "text/html; charset=utf-8");
        });
        app.MapGet("/runs/{runId}", async (string runId, HttpContext context) =>
        {
            try
            {
                var details = await new RunDetailsService(storage).ReadAsync(runId, context.RequestAborted);
                return Results.Content(RenderDetails(details), "text/html; charset=utf-8");
            }
            catch (FileNotFoundException exception)
            {
                return Results.Text(exception.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (ArgumentException exception)
            {
                return Results.Text(exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/runs/{runId}/report", async (string runId, HttpContext context) =>
        {
            var report = await new HtmlReportService(storage).CreateAsync(runId, cancellationToken: context.RequestAborted);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(report.Html), "text/html; charset=utf-8", "performance-report.html");
        });
        app.MapGet("/runs/{runId}/check-current", async (string runId, HttpContext context) =>
        {
            var current = await baselines.GetAsync(BaselineKind.Current, context.RequestAborted);
            if (current is null) return Results.Text("No current baseline is configured.", statusCode: 409);
            var baseline = await archive.ReadAsync(current.RunId, context.RequestAborted);
            var candidate = await archive.ReadAsync(runId, context.RequestAborted);
            var budget = configuration.Load().Budget!;
            var check = new RegressionCheckService().Check(baseline.Evidence, candidate.Evidence, budget);
            return Results.Content(RenderCheck(current.RunId, candidate.RunId, check, budget), "text/html; charset=utf-8");
        });
        app.MapPost("/baselines/current", (Delegate)((HttpContext context) => SelectAsync(context, BaselineKind.Current)));
        app.MapPost("/baselines/anchor", (Delegate)((HttpContext context) => SelectAsync(context, BaselineKind.Anchor)));

        async Task<IResult> SelectAsync(HttpContext context, BaselineKind kind)
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                return Results.Text("Invalid form token. Reload the page and try again.", statusCode: StatusCodes.Status400BadRequest);
            }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (form["runId"].Count != 1)
                return Results.Text("Exactly one RunId is required.", statusCode: StatusCodes.Status400BadRequest);
            try
            {
                await selection.SetAsync(kind, form["runId"].ToString(), "Local UI baseline selection", context.RequestAborted);
            }
            catch (FileNotFoundException exception)
            {
                return Results.Text(exception.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (ArgumentException exception)
            {
                return Results.Text(exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            // POST/redirect/GET prevents refresh from appending another event.
            context.Response.StatusCode = StatusCodes.Status303SeeOther;
            context.Response.Headers.Location = "/";
            return Results.Empty;
        }
        await app.StartAsync(cancellationToken);

        var address = app.Urls.First();
        Console.WriteLine($"Performance Agent UI: {address}");
        if (openBrowser)
            TryOpenBrowser(address);
        await app.WaitForShutdownAsync(cancellationToken);
        return 0;
    }

    private static string Render(IReadOnlyList<ArchivedBenchmarkRun> runs, string? current, string? anchor, IReadOnlyList<BaselineEvent> events, AntiforgeryTokenSet token, WorkspaceStorageStatus storage)
    {
        var rows = string.Join("", runs.OrderByDescending(x => x.Timestamp).Select(run =>
            $"<tr><td><code>{WebUtility.HtmlEncode(run.RunId)}</code><br><a href=\"/runs/{Uri.EscapeDataString(run.RunId)}\">View Details</a> · <a href=\"/runs/{Uri.EscapeDataString(run.RunId)}/check-current\">Check Current</a></td><td>{run.Timestamp.ToString("O", CultureInfo.InvariantCulture)}</td><td>{Label(run.RunId, current, anchor)}</td><td>{(storage.Writable ? SelectionForm(run.RunId, "current", "Make Current", token) + SelectionForm(run.RunId, "anchor", "Make Anchor", token) : "Storage is not writable")}</td></tr>"));
        if (rows.Length == 0) rows = "<tr><td colspan=\"4\">No benchmark runs yet.</td></tr>";
        var eventRows = string.Join("", events.Reverse().Select(item =>
            $"<tr><td>{item.Timestamp.ToString("O", CultureInfo.InvariantCulture)}</td><td>{item.Kind}</td><td>{item.Type}</td><td><code>{WebUtility.HtmlEncode(item.PreviousRunId ?? "—")}</code> → <code>{WebUtility.HtmlEncode(item.RunId)}</code></td></tr>"));
        if (eventRows.Length == 0) eventRows = "<tr><td colspan=\"4\">No baseline events yet.</td></tr>";
        return Page("Performance Agent", $$"""
<h1>Performance Agent</h1><p class="muted">Local performance evidence. CLI remains the primary interface.</p>
<p><a href="/configuration">Effective configuration</a></p>
<section class="card"><h2>Workspace storage</h2><dl>
<dt>Workspace</dt><dd>{{WebUtility.HtmlEncode(storage.WorkspaceDirectory)}}</dd>
<dt>Storage</dt><dd>{{WebUtility.HtmlEncode(storage.StateDirectory)}}</dd>
<dt>Writable</dt><dd>{{(storage.Writable ? "Yes" : "No")}}</dd><dt>Admin</dt><dd>Not required</dd></dl>
{{(storage.Error is null ? "" : $"<p role=\"alert\">{WebUtility.HtmlEncode(storage.Error)}</p>")}}
<p class="muted">Checked with a temporary write probe. No elevation or automatic storage relocation.</p></section>
<div class="cards"><div class="card"><div class="muted">Current baseline</div><strong>{{WebUtility.HtmlEncode(current ?? "Not set")}}</strong></div>
<div class="card"><div class="muted">Anchor baseline</div><strong>{{WebUtility.HtmlEncode(anchor ?? "Not set")}}</strong></div></div>
<h2>Benchmark history</h2><table><thead><tr><th>Run</th><th>Timestamp</th><th>Baseline</th><th>Actions</th></tr></thead><tbody>{{rows}}</tbody></table>
<h2>Baseline timeline</h2><p class="muted">Append-only baseline selection history, newest first.</p><table><thead><tr><th>Timestamp</th><th>Kind</th><th>Event</th><th>Transition</th></tr></thead><tbody>{{eventRows}}</tbody></table>
""");
    }

    private static string RenderDetails(RunDetails details)
    {
        var run = details.Run;
        var environment = run.Evidence.Environment;
        var measurements = string.Join("", run.Evidence.Measurements.OrderBy(x => x.Name, StringComparer.Ordinal).Select(measurement =>
            $"<tr><td>{WebUtility.HtmlEncode(measurement.Name)}</td><td>{measurement.MeanNanoseconds.ToString("G17", CultureInfo.InvariantCulture)}</td><td>{measurement.AllocatedBytesPerOperation?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"}</td></tr>"));
        if (measurements.Length == 0) measurements = "<tr><td colspan=\"3\">No measurements recorded.</td></tr>";
        return Page("Run details — Performance Agent", $$"""
<p><a href="/">Back to history</a></p><h1>Run details</h1>
<p><a href="/runs/{{Uri.EscapeDataString(run.RunId)}}/report">Download HTML report vs Current</a></p>
<dl><dt>RunId</dt><dd>{{WebUtility.HtmlEncode(run.RunId)}}</dd>
<dt>Timestamp</dt><dd>{{run.Timestamp.ToString("O", CultureInfo.InvariantCulture)}}</dd>
<dt>Commit SHA</dt><dd>{{WebUtility.HtmlEncode(run.CommitSha ?? "Unavailable")}}</dd>
<dt>Runtime</dt><dd>{{WebUtility.HtmlEncode(environment?.Runtime ?? "Unavailable")}}</dd>
<dt>OS</dt><dd>{{WebUtility.HtmlEncode(environment?.OperatingSystem ?? "Unavailable")}}</dd>
<dt>Architecture</dt><dd>{{WebUtility.HtmlEncode(environment?.Architecture ?? "Unavailable")}}</dd>
<dt>Active baselines</dt><dd>{{Label(run.RunId, details.IsCurrent ? run.RunId : null, details.IsAnchor ? run.RunId : null)}}</dd></dl>
<h2>Measurements</h2><table><thead><tr><th>Benchmark</th><th>Mean (ns)</th><th>Allocation (B/op)</th></tr></thead><tbody>{{measurements}}</tbody></table>
<p class="muted">Archived evidence is immutable. Unavailable values were not recorded.</p>
""");
    }

    private static string RenderCheck(string baselineRunId, string candidateRunId, EvidenceCheckResult check, PerformanceBudget budget)
    {
        var rows = string.Join("", check.Benchmarks.Select(item => $"<tr><td>{WebUtility.HtmlEncode(item.Name)}</td><td>{(item.Result.Passed ? "PASS" : "REGRESSION")}</td><td>{FormatMetric(item.Result.Comparison.Mean.Baseline)}</td><td>{FormatMetric(item.Result.Comparison.Mean.Candidate)}</td><td>{FormatPercent(item.Result.Comparison.Mean.PercentChange)}</td><td>{FormatMetric(item.Result.Comparison.AllocatedBytes.Baseline)}</td><td>{FormatMetric(item.Result.Comparison.AllocatedBytes.Candidate)}</td><td>{FormatPercent(item.Result.Comparison.AllocatedBytes.PercentChange)}</td></tr>"));
        return Page("Regression check — Performance Agent", $"<p><a href=\"/\">Back to history</a></p><h1>{(check.Passed ? "PASS" : "REGRESSION")}</h1><p>Candidate <code>{WebUtility.HtmlEncode(candidateRunId)}</code> vs Current <code>{WebUtility.HtmlEncode(baselineRunId)}</code></p><p class=\"muted\">Budget: mean +{budget.MaxMeanRegressionPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? "not configured"}%, allocation +{budget.MaxAllocationRegressionPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? "not configured"}% (perfagent.json or defaults).</p><table><thead><tr><th>Benchmark</th><th>Status</th><th>Mean baseline (ns)</th><th>Mean candidate (ns)</th><th>Mean change</th><th>Allocation baseline (B/op)</th><th>Allocation candidate (B/op)</th><th>Allocation change</th></tr></thead><tbody>{rows}</tbody></table>");
    }

    private static string FormatMetric(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Unavailable";

    private static string FormatPercent(double? value)
    {
        if (value is null) return "Unavailable";
        return value.Value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Page(string title, string content)
    {
        const string template = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>__TITLE__</title><style>
body{font:15px system-ui;margin:0;background:#f6f7f9;color:#1f2937}main{max-width:1000px;margin:48px auto;padding:0 24px}
h1{font-size:28px}.cards{display:grid;grid-template-columns:repeat(2,1fr);gap:16px;margin:24px 0}
.card,table{background:white;border:1px solid #e5e7eb;border-radius:10px}.card{padding:18px}.muted{color:#6b7280}
table{width:100%;border-collapse:collapse;overflow:hidden}th,td{text-align:left;padding:13px;border-bottom:1px solid #eee}th{background:#fafafa}
.badge{display:inline-block;padding:3px 8px;border-radius:999px;background:#eef2ff;margin-right:5px}code{font-size:13px}
form{display:inline-block;margin:3px}button{cursor:pointer;padding:6px 10px}
dt{font-weight:600;margin-top:10px}dd{margin:4px 0;overflow-wrap:anywhere}p[role=alert]{color:#991b1b}
</style></head><body><main>__CONTENT__</main></body></html>
""";
        return template.Replace("__TITLE__", WebUtility.HtmlEncode(title), StringComparison.Ordinal)
            .Replace("__CONTENT__", content, StringComparison.Ordinal);
    }

    private static string SelectionForm(string runId, string kind, string label, AntiforgeryTokenSet token) =>
        $"<form method=\"post\" action=\"/baselines/{kind}\"><input type=\"hidden\" name=\"runId\" value=\"{WebUtility.HtmlEncode(runId)}\"><input type=\"hidden\" name=\"{WebUtility.HtmlEncode(token.FormFieldName)}\" value=\"{WebUtility.HtmlEncode(token.RequestToken)}\"><button type=\"submit\">{label}</button></form>";

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
