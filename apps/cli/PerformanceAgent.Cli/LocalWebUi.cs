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
using PerformanceAgent.Core.Analysis;

namespace PerformanceAgent.Cli;

internal static class LocalWebUi
{
    public static async Task<int> RunAsync(bool openBrowser, CancellationToken cancellationToken)
    {
        await using var app = await StartAsync(WorkspaceStorage.Resolve(), createProvider: null, cancellationToken);
        var address = app.Urls.First();
        Console.WriteLine($"Performance Agent UI: {address}");
        if (openBrowser)
            TryOpenBrowser(address);
        await app.WaitForShutdownAsync(cancellationToken);
        return 0;
    }

    /// <summary>
    /// Builds and starts the loopback UI. <paramref name="createProvider"/> is passed unchanged to the shared
    /// <see cref="AnalyzeService"/>; null uses the production provider factory, exactly like the CLI.
    /// </summary>
    internal static async Task<WebApplication> StartAsync(
        WorkspaceStorage storage,
        Func<string, string?, IPerformanceAnalysisProvider>? createProvider,
        CancellationToken cancellationToken)
    {
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
        var app = builder.Build();
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
            var content = $"<p><a href=\"/\">Back to history</a></p><h1>Effective configuration</h1><dl><dt>Configuration file</dt><dd>{WebUtility.HtmlEncode(effective.Path)}</dd><dt>Budget source</dt><dd>{WebUtility.HtmlEncode(effective.BudgetSource)}</dd><dt>Max mean regression (%)</dt><dd>{effective.Budget.MaxMeanRegressionPercent?.ToString(CultureInfo.InvariantCulture) ?? "Not configured"}</dd><dt>Max allocation regression (%)</dt><dd>{effective.Budget.MaxAllocationRegressionPercent?.ToString(CultureInfo.InvariantCulture) ?? "Not configured"}</dd><dt>AI provider</dt><dd>{WebUtility.HtmlEncode(effective.AiProvider)}</dd><dt>AI model</dt><dd>{WebUtility.HtmlEncode(effective.AiModel ?? "Not configured")}</dd></dl><p>Explicit CLI check thresholds or --budget override workspace settings. Do not store secrets in perfagent.json.</p><p class=\"muted\">AI credentials are read only from the environment (OpenAI: OPENAI_API_KEY) and are never displayed here.</p>";
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
                var current = await baselines.GetAsync(BaselineKind.Current, context.RequestAborted);
                DeterministicAnalysisResult? measured = null;
                string? measuredError = null;
                if (current is not null)
                {
                    // Deterministic phase only: no provider is created and nothing is sent anywhere.
                    try { measured = await new AnalyzeService(storage, createProvider).CheckAsync(runId, context.RequestAborted); }
                    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException)
                    {
                        measuredError = exception.Message;
                    }
                }
                var token = antiforgery.GetAndStoreTokens(context);
                return Results.Content(RenderDetails(details, current?.RunId, measured, measuredError, token), "text/html; charset=utf-8");
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
        // POST only: analysis may call a paid external provider, so it needs an explicit, CSRF-protected click.
        app.MapPost("/runs/{runId}/analyze", async (string runId, HttpContext context) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                return Results.Text("Invalid form token. Reload the page and try again.", statusCode: StatusCodes.Status400BadRequest);
            }
            AnalyzeResult result;
            try
            {
                result = await new AnalyzeService(storage, createProvider).AnalyzeAsync(runId, context.RequestAborted);
            }
            catch (FileNotFoundException exception)
            {
                return Results.Text(exception.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (ArgumentException exception)
            {
                return Results.Text(exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                // Failed before a measured result existed (e.g. no Current baseline); no provider was created.
                return Results.Content(RenderAnalysisNotStarted(runId, exception.Message), "text/html; charset=utf-8", statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Content(RenderAnalysis(result), "text/html; charset=utf-8");
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
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
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

    private static string RenderDetails(RunDetails details, string? currentRunId, DeterministicAnalysisResult? measured, string? measuredError, AntiforgeryTokenSet token)
    {
        var measuredResult = measured is not null
            ? $"<strong>{AnalysisConsoleWriter.Verdict(measured)}</strong>"
            : currentRunId is null
                ? "Not available: set a Current baseline first. Anchor is not used as a fallback."
                : $"Not available: {WebUtility.HtmlEncode(measuredError ?? "")}";
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
<section class="card"><h2>Measured result vs Current</h2><dl>
<dt>Current baseline</dt><dd>{{(currentRunId is null ? "Not set" : $"<code>{WebUtility.HtmlEncode(currentRunId)}</code>")}}</dd>
<dt>Deterministic result</dt><dd>{{measuredResult}}</dd></dl>
<form method="post" action="/runs/{{Uri.EscapeDataString(run.RunId)}}/analyze"><input type="hidden" name="{{WebUtility.HtmlEncode(token.FormFieldName)}}" value="{{WebUtility.HtmlEncode(token.RequestToken)}}"><button type="submit">Analyze with AI</button></form>
<p class="muted">Sends this run's and the Current baseline's normalized measurements, the budget and the measured verdicts to the configured AI provider (<a href="/configuration">ai.provider / ai.model</a>). Provider charges may apply. The result is advisory, not saved, and never changes the measured result.</p></section>
""");
    }

    private static string RenderAnalysisNotStarted(string runId, string message) =>
        Page("Analysis — Performance Agent", $"<p><a href=\"/\">Back to history</a> · <a href=\"/runs/{Uri.EscapeDataString(runId)}\">Run details</a></p><h1>Analysis could not start</h1><p role=\"alert\">{WebUtility.HtmlEncode(message)}</p><p class=\"muted\">No measured result was produced and no AI provider was contacted. Archive, baselines and history were not changed.</p>");

    // Every value from evidence, configuration, or the model is HTML-encoded, and model text is also stripped of
    // control characters. Nothing is rendered as HTML or Markdown, and the CSP forbids scripts regardless.
    private static string RenderAnalysis(AnalyzeResult result)
    {
        var deterministic = result.Deterministic;
        var budget = deterministic.Budget;
        var verdict = AnalysisConsoleWriter.Verdict(deterministic);
        var ai = result.Analysis is { } analysis
            ? $$"""
<section class="card ai"><h2>AI analysis</h2><p class="muted">Advisory · generated by a model, not measured. It cannot change the measured result above.</p>
<h3>Summary</h3><p class="ai-text">{{Text(analysis.Summary)}}</p>
<h3>Measured evidence cited by the model (values from Performance Agent's measurements, not the model)</h3>{{CitedEvidence(analysis.EvidenceReferences, deterministic)}}
<h3>Hypotheses (unverified; not measured facts)</h3>{{List(analysis.Hypotheses.Select(x => x.Rationale is null ? Text(x.Statement) : $"{Text(x.Statement)}<br><span class=\"muted\">Rationale: {Text(x.Rationale)}</span>"))}}
<h3>Suggested experiments (verify by benchmarking)</h3>{{List(analysis.SuggestedExperiments.Select(x => x.ExpectedSignal is null ? Text(x.Description) : $"{Text(x.Description)}<br><span class=\"muted\">Expected signal: {Text(x.ExpectedSignal)}</span>"))}}
<h3>Uncertainty</h3><p class="ai-text">{{(analysis.Uncertainty is { } uncertainty && !string.IsNullOrWhiteSpace(uncertainty) ? Text(uncertainty) : "None stated.")}}</p></section>
"""
            : $"<section class=\"card ai\" role=\"alert\"><h2>AI analysis failed</h2><p class=\"ai-text\">{Text(result.AnalysisError ?? "Unknown error.")}</p><p class=\"muted\">Only the AI analysis failed. The measured result above was computed successfully and is unaffected.</p></section>";

        return Page("Analysis — Performance Agent", $$"""
<p><a href="/">Back to history</a> · <a href="/runs/{{Uri.EscapeDataString(deterministic.CandidateRunId)}}">Run details</a></p><h1>Performance analysis</h1>
<dl><dt>Candidate</dt><dd><code>{{WebUtility.HtmlEncode(deterministic.CandidateRunId)}}</code></dd>
<dt>Current baseline</dt><dd><code>{{WebUtility.HtmlEncode(deterministic.BaselineRunId)}}</code></dd>
<dt>Budget</dt><dd>mean {{Threshold(budget.MaxMeanRegressionPercent)}}, allocation {{Threshold(budget.MaxAllocationRegressionPercent)}} ({{WebUtility.HtmlEncode(deterministic.BudgetSource)}})</dd></dl>
<section class="card measured"><h2>Measured result</h2><p class="verdict">{{verdict}}</p><p class="muted">Deterministic and authoritative: computed from archived measurements before any AI analysis.</p>
<h3>Measured regressions</h3>{{MeasuredTable(deterministic.Check.Benchmarks.Where(x => !x.Result.Passed), budget)}}
<h3>Measured within budget</h3>{{MeasuredTable(deterministic.Check.Benchmarks.Where(x => x.Result.Passed), budget)}}</section>
{{ai}}
<p class="muted">Analysis is not saved. Archive, baselines and history were not changed.</p>
""");

        static string Text(string value) => WebUtility.HtmlEncode(AnalysisConsoleWriter.Sanitize(value));

        static string List(IEnumerable<string> encodedItems)
        {
            var entries = string.Join("", encodedItems.Select(item => $"<li>{item}</li>"));
            return entries.Length == 0 ? "<p class=\"muted\">None.</p>" : $"<ul class=\"ai-text\">{entries}</ul>";
        }
    }

    // Citations show measured values computed from evidence; the model's observation is only a labelled, single-line note.
    private static string CitedEvidence(IReadOnlyList<AnalysisEvidenceReference> references, DeterministicAnalysisResult deterministic)
    {
        if (references.Count == 0)
            return "<p class=\"muted\">None.</p>";
        var measured = deterministic.Check.Benchmarks.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var items = references.Select(reference =>
        {
            var values = measured.TryGetValue(reference.BenchmarkName, out var item) && CheckFormatting.FormatMeasured(item.Result, deterministic.Budget) is var (mean, allocation)
                ? $"<br>Mean: {WebUtility.HtmlEncode(mean)}<br>Allocation: {WebUtility.HtmlEncode(allocation)}"
                : "<br>No measured comparison available.";
            return $"<li><code>{WebUtility.HtmlEncode(reference.BenchmarkName)}</code>{values}<br><span class=\"muted\">AI note:</span> {WebUtility.HtmlEncode(AnalysisConsoleWriter.SingleLine(reference.Observation))}</li>";
        });
        return $"<ul>{string.Join("", items)}</ul>";
    }

    private static string MeasuredTable(IEnumerable<BenchmarkCheckResult> items, PerformanceBudget budget)
    {
        var rows = string.Join("", items.Select(item =>
        {
            var (mean, allocation) = CheckFormatting.FormatMeasured(item.Result, budget);
            return $"<tr><td>{WebUtility.HtmlEncode(item.Name)}</td><td>{(item.Result.Passed ? "PASS" : "FAIL")}</td><td>{WebUtility.HtmlEncode(mean)}</td><td>{WebUtility.HtmlEncode(allocation)}</td></tr>";
        }));
        return rows.Length == 0
            ? "<p>None.</p>"
            : $"<table><thead><tr><th>Benchmark</th><th>Status</th><th>Mean</th><th>Allocation</th></tr></thead><tbody>{rows}</tbody></table>";
    }

    private static string Threshold(double? percent) =>
        percent is null ? "not configured" : $"+{percent.Value.ToString("0.##", CultureInfo.InvariantCulture)}%";

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
.measured{border-left:5px solid #1f2937;margin:20px 0}.verdict{font-size:26px;font-weight:700;margin:6px 0}
.ai{border:1px dashed #9ca3af;background:#fbfbfc;margin:20px 0}.ai-text{white-space:pre-wrap;overflow-wrap:anywhere}section[role=alert] h2{color:#991b1b}
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
