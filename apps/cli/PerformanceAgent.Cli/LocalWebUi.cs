using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using PerformanceAgent.Core.History;

namespace PerformanceAgent.Cli;

internal static class LocalWebUi
{
    public static async Task<int> RunAsync(bool openBrowser, CancellationToken cancellationToken)
    {
        var storage = WorkspaceStorage.Resolve();
        var archive = new FileRunArchive(storage.StateDirectory);
        var baselines = new FileBaselineStore(storage.StateDirectory);
        var selection = new BaselineSelectionService(storage);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
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
        app.MapGet("/", async (HttpContext context) =>
        {
            var runs = await archive.ListAsync(context.RequestAborted);
            var current = await baselines.GetAsync(BaselineKind.Current, context.RequestAborted);
            var anchor = await baselines.GetAsync(BaselineKind.Anchor, context.RequestAborted);
            var token = antiforgery.GetAndStoreTokens(context);
            return Results.Content(Render(runs, current?.RunId, anchor?.RunId, token), "text/html; charset=utf-8");
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

    private static string Render(IReadOnlyList<ArchivedBenchmarkRun> runs, string? current, string? anchor, AntiforgeryTokenSet token)
    {
        var rows = string.Join("", runs.OrderByDescending(x => x.Timestamp).Select(run =>
            $"<tr><td><code>{WebUtility.HtmlEncode(run.RunId)}</code></td><td>{run.Timestamp:O}</td><td>{Label(run.RunId, current, anchor)}</td><td>{SelectionForm(run.RunId, "current", "Make Current", token)}{SelectionForm(run.RunId, "anchor", "Make Anchor", token)}</td></tr>"));
        if (rows.Length == 0) rows = "<tr><td colspan=\"4\">No benchmark runs yet.</td></tr>";
        return $$"""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Performance Agent</title><style>
body{font:15px system-ui;margin:0;background:#f6f7f9;color:#1f2937}main{max-width:1000px;margin:48px auto;padding:0 24px}
h1{font-size:28px}.cards{display:grid;grid-template-columns:repeat(2,1fr);gap:16px;margin:24px 0}
.card,table{background:white;border:1px solid #e5e7eb;border-radius:10px}.card{padding:18px}.muted{color:#6b7280}
table{width:100%;border-collapse:collapse;overflow:hidden}th,td{text-align:left;padding:13px;border-bottom:1px solid #eee}th{background:#fafafa}
.badge{display:inline-block;padding:3px 8px;border-radius:999px;background:#eef2ff;margin-right:5px}code{font-size:13px}
form{display:inline-block;margin:3px}button{cursor:pointer;padding:6px 10px}
</style></head><body><main><h1>Performance Agent</h1><p class="muted">Local performance evidence. CLI remains the primary interface.</p>
<div class="cards"><div class="card"><div class="muted">Current baseline</div><strong>{{WebUtility.HtmlEncode(current ?? "Not set")}}</strong></div>
<div class="card"><div class="muted">Anchor baseline</div><strong>{{WebUtility.HtmlEncode(anchor ?? "Not set")}}</strong></div></div>
<h2>Benchmark history</h2><table><thead><tr><th>Run</th><th>Timestamp</th><th>Baseline</th><th>Actions</th></tr></thead><tbody>{{rows}}</tbody></table>
</main></body></html>
""";
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
