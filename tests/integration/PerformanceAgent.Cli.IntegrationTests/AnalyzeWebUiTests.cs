using System.Net;
using System.Text.RegularExpressions;
using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.History;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class AnalyzeWebUiTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    // 1, 3: Run Details offers an explicit POST action and shows the measured verdict without involving AI.
    [Fact]
    public async Task Run_details_exposes_analyze_post_action_and_never_invokes_ai()
    {
        await ArrangeRegressionOf25PercentAsync();
        var provider = RecordingProvider.Returning(Analyses.Claiming("x"));
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => provider);

        var (status, page, _) = await ui.GetAsync("/runs/run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<form method=\"post\" action=\"/runs/run-candidate/analyze\">", page, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", page, StringComparison.Ordinal);
        Assert.Contains("<button type=\"submit\">Analyze with AI</button>", page, StringComparison.Ordinal);
        Assert.Contains("<dt>Current baseline</dt><dd><code>run-current</code></dd>", page, StringComparison.Ordinal);
        Assert.Contains("<dt>Deterministic result</dt><dd><strong>REGRESSION</strong></dd>", page, StringComparison.Ordinal);
        Assert.Empty(ui.Resolutions);
        Assert.Empty(provider.Requests);
    }

    // 2: analysis cannot be triggered by GET.
    [Fact]
    public async Task Analyze_is_post_only()
    {
        await ArrangeRegressionOf25PercentAsync();
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));

        var (status, _, _) = await ui.GetAsync("/runs/run-candidate/analyze");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
        Assert.Empty(ui.Resolutions);
    }

    // 4, 5, 6: the POST goes through the shared AnalyzeService with the archived candidate, Current baseline and configuration.
    [Fact]
    public async Task Analyze_uses_shared_service_with_candidate_current_baseline_and_configured_provider()
    {
        var current = await _workspace.ArchiveAsync("run-current", 100, 64);
        await _workspace.ArchiveAsync("run-anchor", 10, 8);
        var candidate = await _workspace.ArchiveAsync("run-candidate", 125, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Anchor, "run-anchor");
        await _workspace.SetBaselineAsync(BaselineKind.Current, "run-current");
        _workspace.WriteConfiguration(new { ai = new { provider = "OpenAI", model = "configured-model" } });
        var provider = RecordingProvider.Returning(Analyses.Claiming("Mean increased."));
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => provider);

        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { ("OpenAI", (string?)"configured-model") }, ui.Resolutions);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(candidate.Measurements, request.Candidate.Measurements);
        Assert.Equal(current.Measurements, request.Baseline.Measurements);
        Assert.Equal(new PerformanceRegressionResult("Sample.Work", Passed: false, MeanExceeded: true, AllocationExceeded: false), Assert.Single(request.RegressionResults!));
        Assert.Contains("<dt>Candidate</dt><dd><code>run-candidate</code></dd>", page, StringComparison.Ordinal);
        Assert.Contains("<dt>Current baseline</dt><dd><code>run-current</code></dd>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("run-anchor", page, StringComparison.Ordinal);
        Assert.True(provider.Disposed);
    }

    // 20 (parity): the UI and `perfagent analyze` produce the same deterministic result from the same workspace.
    [Fact]
    public async Task Ui_and_cli_analyze_report_the_same_measured_result()
    {
        await ArrangeRegressionOf25PercentAsync();
        var analysis = Analyses.Claiming("Mean increased.");
        var cli = await new AnalyzeService(_workspace.Storage, (_, _) => RecordingProvider.Returning(analysis))
            .AnalyzeAsync("run-candidate", CancellationToken.None);
        using var console = new StringWriter();
        AnalysisConsoleWriter.Write(console, cli);
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(analysis));

        var (_, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Contains($"Deterministic result: {AnalysisConsoleWriter.Verdict(cli.Deterministic)}", console.ToString(), StringComparison.Ordinal);
        Assert.Contains($"<p class=\"verdict\">{AnalysisConsoleWriter.Verdict(cli.Deterministic)}</p>", page, StringComparison.Ordinal);
        foreach (var item in cli.Deterministic.Check.Benchmarks)
        {
            var mean = CheckFormatting.FormatChange(item.Result.Comparison.Mean) + CheckFormatting.FormatBudget(cli.Deterministic.Budget.MaxMeanRegressionPercent, item.Result.MeanExceeded);
            Assert.Contains($"    Mean: {mean}", console.ToString(), StringComparison.Ordinal);
            Assert.Contains($"<td>{WebUtility.HtmlEncode(mean)}</td>", page, StringComparison.Ordinal);
        }
        Assert.Contains("<td>100 -&gt; 125 (+25%) (budget +5%) FAIL</td>", page, StringComparison.Ordinal);
    }

    // 7: Anchor is never used when Current is missing; nothing reaches a provider.
    [Fact]
    public async Task Missing_current_baseline_is_clear_and_anchor_is_not_a_fallback()
    {
        await _workspace.ArchiveAsync("run-anchor", 100, 64);
        await _workspace.ArchiveAsync("run-candidate", 125, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Anchor, "run-anchor");
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));

        var (_, details, _) = await ui.GetAsync("/runs/run-candidate");
        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Contains("<dt>Current baseline</dt><dd>Not set</dd>", details, StringComparison.Ordinal);
        Assert.Contains("Anchor is not used as a fallback", details, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("<h1>Analysis could not start</h1>", page, StringComparison.Ordinal);
        Assert.Contains("requires a Current baseline", page, StringComparison.Ordinal);
        Assert.Contains("no AI provider was contacted", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"verdict\"", page, StringComparison.Ordinal);
        Assert.Empty(ui.Resolutions);
    }

    [Fact]
    public async Task Missing_candidate_returns_not_found_without_ai()
    {
        await ArrangeRegressionOf25PercentAsync();
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));
        var token = await ui.TokenFromDetailsAsync("run-candidate");

        var (status, body, _) = await ui.PostAnalyzeAsync("run-missing", token);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("Archived benchmark run 'run-missing' was not found.", body, StringComparison.Ordinal);
        Assert.Empty(ui.Resolutions);
    }

    // 8: workspace budget decides the measured verdict.
    [Fact]
    public async Task Workspace_budget_is_respected()
    {
        await ArrangeRegressionOf25PercentAsync();
        _workspace.WriteConfiguration(new { budget = new { maxMeanRegressionPercent = 50, maxAllocationRegressionPercent = 50 } });
        var provider = RecordingProvider.Returning(Analyses.Claiming("x"));
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => provider);

        var (_, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(new PerformanceBudget(50, 50), Assert.Single(provider.Requests).Budget);
        Assert.Contains("<p class=\"verdict\">PASS</p>", page, StringComparison.Ordinal);
        Assert.Contains("mean +50%, allocation +50%", page, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode(Path.Combine(_workspace.Directory, "perfagent.json")), page, StringComparison.Ordinal);
    }

    // 9, 10: AI prose never changes the authoritative verdict, which is rendered once, outside the AI section.
    [Theory]
    [InlineData(101, "PASS", "REGRESSION: Sample.Work is severely slower and fails its budget.")]
    [InlineData(125, "REGRESSION", "PASS: no regression; all benchmarks are within budget.")]
    public async Task Measured_verdict_stays_authoritative_regardless_of_ai_prose(double candidateMean, string verdict, string prose)
    {
        await _workspace.ArchiveAsync("run-current", 100, 64);
        await _workspace.ArchiveAsync("run-candidate", candidateMean, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Current, "run-current");
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming(prose)));

        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(Regex.Matches(page, "class=\"verdict\""));
        Assert.Contains($"<section class=\"card measured\"><h2>Measured result</h2><p class=\"verdict\">{verdict}</p>", page, StringComparison.Ordinal);
        var aiSection = page.IndexOf("<section class=\"card ai\"><h2>AI analysis</h2>", StringComparison.Ordinal);
        Assert.True(aiSection > page.IndexOf("class=\"verdict\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf(WebUtility.HtmlEncode(prose), StringComparison.Ordinal) > aiSection);
        Assert.Contains("Advisory · generated by a model, not measured.", page, StringComparison.Ordinal);
        Assert.Contains("Hypotheses (unverified; not measured facts)", page, StringComparison.Ordinal);
    }

    // 11: provider failure keeps the measured result and says only AI failed.
    [Theory]
    [InlineData("rate-limit")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task Provider_failure_still_shows_measured_result(string kind)
    {
        await ArrangeRegressionOf25PercentAsync();
        Exception failure = kind switch
        {
            "rate-limit" => new InvalidOperationException("OpenAI rate limit or quota exceeded (HTTP 429). Retry later. Benchmark results are unaffected."),
            "timeout" => new TimeoutException("OpenAI analysis did not complete within 90 seconds. Benchmark results are unaffected."),
            _ => new InvalidOperationException("OpenAI returned malformed structured analysis: AI analysis summary is required."),
        };
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => new RecordingProvider((_, _) => Task.FromException<PerformanceAnalysis>(failure)));

        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<dt>Candidate</dt><dd><code>run-candidate</code></dd>", page, StringComparison.Ordinal);
        Assert.Contains("<dt>Current baseline</dt><dd><code>run-current</code></dd>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"verdict\">REGRESSION</p>", page, StringComparison.Ordinal);
        Assert.Contains("<td>Sample.Work</td><td>FAIL</td><td>100 -&gt; 125 (+25%) (budget +5%) FAIL</td>", page, StringComparison.Ordinal);
        Assert.Contains("<section class=\"card ai\" role=\"alert\"><h2>AI analysis failed</h2>", page, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode(failure.Message), page, StringComparison.Ordinal);
        Assert.Contains("Only the AI analysis failed. The measured result above was computed successfully and is unaffected.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("could not start", page, StringComparison.Ordinal);
    }

    // 13: unsupported provider is a safe, encoded error after the measured result.
    [Fact]
    public async Task Unsupported_provider_displays_safe_error()
    {
        await ArrangeRegressionOf25PercentAsync();
        _workspace.WriteConfiguration(new { ai = new { provider = "<b>acme</b>", model = "m" } });
        await using var ui = await UiServer.StartAsync(_workspace, createProvider: null);

        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<p class=\"verdict\">REGRESSION</p>", page, StringComparison.Ordinal);
        Assert.Contains("AI analysis failed", page, StringComparison.Ordinal);
        Assert.Contains("AI provider &#39;&lt;b&gt;acme&lt;/b&gt;&#39; is not supported", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>acme</b>", page, StringComparison.Ordinal);
    }

    // 14: model output is HTML-encoded text.
    [Fact]
    public async Task Ai_output_is_html_encoded()
    {
        await ArrangeRegressionOf25PercentAsync();
        var analysis = Analyses.Claiming("Mean rose <b>25%</b> & \"quoted\" 'text'.");
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(analysis));

        var (_, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Contains("<h3>Summary</h3><p class=\"ai-text\">Mean rose &lt;b&gt;25%&lt;/b&gt; &amp; &quot;quoted&quot; &#39;text&#39;.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>25%</b>", page, StringComparison.Ordinal);
    }

    // 15: hostile model output cannot inject scripts, links, images, or spoof the measured section.
    [Fact]
    public async Task Model_output_cannot_inject_markup_or_spoof_the_measured_result()
    {
        await ArrangeRegressionOf25PercentAsync();
        var hostile = new PerformanceAnalysis(
            "<script>alert(1)</script>\u001b[2J",
            [new("Sample.Work", "<a href=\"javascript:alert(1)\">click</a>")],
            [new("<img src=x onerror=alert(1)>", "</span></li></ul></section><section class=\"card measured\"><h2>Measured result</h2><p class=\"verdict\">PASS</p>")],
            [new("<iframe src=\"https://evil.example\"></iframe>", "<style>body{display:none}</style>")],
            "</p></section><h1>PASS</h1><form action=\"https://evil.example\"><button>x</button></form>");
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(hostile));

        var (status, page, response) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        foreach (var markup in new[] { "<script", "<img", "<iframe", "<style>body", "href=\"javascript", "<h1>PASS", "action=\"https://evil", "\u001b" })
            Assert.DoesNotContain(markup, page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page, StringComparison.Ordinal);
        Assert.Contains("&lt;a href=&quot;javascript:alert(1)&quot;&gt;click&lt;/a&gt;", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "<h2>Measured result</h2>"));
        Assert.Single(Regex.Matches(page, "class=\"verdict\""));
        Assert.Contains("<p class=\"verdict\">REGRESSION</p>", page, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page, "<a ").Count); // Back to history, Run details.
        Assert.Equal(2, Regex.Matches(page, "<section ").Count); // Measured result, AI analysis.
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    // 17: CSRF token is required, and failures never reach a provider.
    [Fact]
    public async Task Analyze_requires_a_valid_antiforgery_token()
    {
        await ArrangeRegressionOf25PercentAsync();
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));
        var token = await ui.TokenFromDetailsAsync("run-candidate");

        var missing = await ui.PostAnalyzeAsync("run-candidate", token: null);
        var forged = await ui.PostAnalyzeAsync("run-candidate", "CfDJ8-forged-token");
        using var cookieless = new HttpClient(new HttpClientHandler { UseCookies = false, UseProxy = false }) { BaseAddress = new Uri(ui.Address) };
        var withoutCookie = await cookieless.PostAsync("/runs/run-candidate/analyze",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Contains("Invalid form token", missing.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, forged.Status);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCookie.StatusCode);
        Assert.Empty(ui.Resolutions);
    }

    // 18: Origin, Sec-Fetch-Site and Host protections apply to the Analyze POST.
    [Theory]
    [InlineData("Origin", "https://evil.example")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Host", "evil.example")]
    public async Task Cross_origin_or_foreign_host_requests_are_rejected(string header, string value)
    {
        await ArrangeRegressionOf25PercentAsync();
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));
        var token = await ui.TokenFromDetailsAsync("run-candidate");

        var (status, body, _) = await ui.PostAnalyzeAsync("run-candidate", token, new Dictionary<string, string> { [header] = value });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("Only same-origin loopback requests are allowed.", body, StringComparison.Ordinal);
        Assert.Empty(ui.Resolutions);
    }

    [Fact]
    public async Task Same_origin_post_is_accepted()
    {
        await ArrangeRegressionOf25PercentAsync();
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));
        var token = await ui.TokenFromDetailsAsync("run-candidate");

        var (status, _, _) = await ui.PostAnalyzeAsync("run-candidate", token,
            new Dictionary<string, string> { ["Origin"] = ui.Address, ["Sec-Fetch-Site"] = "same-origin" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(ui.Resolutions);
    }

    // 19: no UI interaction writes archive, baselines, or events.
    [Fact]
    public async Task Workspace_state_stays_byte_identical()
    {
        await ArrangeRegressionOf25PercentAsync();
        await _workspace.SetBaselineAsync(BaselineKind.Anchor, "run-current");
        var before = _workspace.SnapshotState();
        var calls = 0;
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => ++calls % 2 == 1
            ? RecordingProvider.Returning(Analyses.Claiming("x"))
            : new RecordingProvider((_, _) => Task.FromException<PerformanceAnalysis>(new InvalidOperationException("down"))));

        await ui.GetAsync("/runs/run-candidate");
        await ui.GetAsync("/configuration");
        Assert.Equal(HttpStatusCode.OK, (await ui.AnalyzeAsync("run-candidate")).Status);
        Assert.Equal(HttpStatusCode.OK, (await ui.AnalyzeAsync("run-candidate")).Status);
        await ui.PostAnalyzeAsync("run-candidate", token: null);
        await ui.PostAnalyzeAsync("run-candidate", await ui.TokenFromDetailsAsync("run-candidate"), new Dictionary<string, string> { ["Origin"] = "https://evil.example" });

        Assert.Equal(2, calls);
        Assert.Equal(before, _workspace.SnapshotState());
    }

    // Configuration parity: the UI shows the same non-secret AI settings as `perfagent config show`.
    [Fact]
    public async Task Configuration_page_shows_effective_ai_provider_and_model()
    {
        _workspace.WriteConfiguration(new { ai = new { provider = "OpenAI", model = "<model & co>" } });
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));

        var (status, page, _) = await ui.GetAsync("/configuration");
        var effective = new WorkspaceConfiguration(_workspace.Storage).Inspect();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains($"<dt>AI provider</dt><dd>{WebUtility.HtmlEncode(effective.AiProvider)}</dd>", page, StringComparison.Ordinal);
        Assert.Contains("<dt>AI model</dt><dd>&lt;model &amp; co&gt;</dd>", page, StringComparison.Ordinal);
        Assert.Empty(ui.Resolutions);
    }

    [Fact]
    public async Task Configuration_page_shows_defaults_without_ai_settings()
    {
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));

        var (_, page, _) = await ui.GetAsync("/configuration");

        Assert.Contains("<dt>AI provider</dt><dd>openai</dd>", page, StringComparison.Ordinal);
        Assert.Contains("<dt>AI model</dt><dd>Not configured</dd>", page, StringComparison.Ordinal);
    }

    private async Task ArrangeRegressionOf25PercentAsync()
    {
        await _workspace.ArchiveAsync("run-current", 100, 64);
        await _workspace.ArchiveAsync("run-candidate", 125, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Current, "run-current");
    }
}

/// <summary>Tests that set OPENAI_API_KEY in the process; isolated from parallel tests.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AnalyzeWebUiCredentialTests : IDisposable
{
    private const string SecretKey = "sk-test-UI-MUST-NOT-RENDER-91c2e";
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    // 12: missing model / API key are safe, clear errors (production provider factory, no network).
    [Fact]
    public async Task Missing_model_displays_safe_error()
    {
        using var _ = new EnvironmentVariableScope("OPENAI_API_KEY", SecretKey);
        await ArrangeRegressionAsync();
        await using var ui = await UiServer.StartAsync(_workspace, createProvider: null);

        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<p class=\"verdict\">REGRESSION</p>", page, StringComparison.Ordinal);
        Assert.Contains("AI analysis failed", page, StringComparison.Ordinal);
        Assert.Contains("Set &quot;ai.model&quot; in perfagent.json", page, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_api_key_displays_safe_error()
    {
        using var _ = new EnvironmentVariableScope("OPENAI_API_KEY", null);
        await ArrangeRegressionAsync();
        _workspace.WriteConfiguration(new { ai = new { provider = "openai", model = "configured-model" } });
        await using var ui = await UiServer.StartAsync(_workspace, createProvider: null);

        var (status, page, _) = await ui.AnalyzeAsync("run-candidate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<p class=\"verdict\">REGRESSION</p>", page, StringComparison.Ordinal);
        Assert.Contains("requires the OPENAI_API_KEY environment variable", page, StringComparison.Ordinal);
    }

    // 16: the credential value never appears, and the UI does not reveal whether it is set.
    [Fact]
    public async Task Credential_never_appears_in_any_page_or_error()
    {
        await ArrangeRegressionAsync();
        var bodies = new List<string>();
        string configurationWithoutKey;
        using (new EnvironmentVariableScope("OPENAI_API_KEY", null))
        await using (var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x"))))
            configurationWithoutKey = (await ui.GetAsync("/configuration")).Body;

        using (new EnvironmentVariableScope("OPENAI_API_KEY", SecretKey))
        {
            await using (var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x"))))
            {
                var configuration = (await ui.GetAsync("/configuration")).Body;
                Assert.Equal(configurationWithoutKey, configuration);
                bodies.Add(configuration);
                bodies.Add((await ui.GetAsync("/")).Body);
                bodies.Add((await ui.GetAsync("/runs/run-candidate")).Body);
                bodies.Add((await ui.AnalyzeAsync("run-candidate")).Body);
                bodies.Add((await ui.PostAnalyzeAsync("run-candidate", "forged")).Body);
            }

            await using (var ui = await UiServer.StartAsync(_workspace, createProvider: null))
            {
                bodies.Add((await ui.AnalyzeAsync("run-candidate")).Body); // Missing model with the real factory.
                _workspace.WriteConfiguration(new { ai = new { provider = "acme", model = "m" } });
                bodies.Add((await ui.AnalyzeAsync("run-candidate")).Body); // Unsupported provider.
            }
        }

        Assert.Equal(7, bodies.Count);
        Assert.All(bodies, body => Assert.DoesNotContain(SecretKey, body, StringComparison.Ordinal));
        Assert.All(bodies, body => Assert.DoesNotContain("sk-test", body, StringComparison.Ordinal));
    }

    private async Task ArrangeRegressionAsync()
    {
        await _workspace.ArchiveAsync("run-current", 100, 64);
        await _workspace.ArchiveAsync("run-candidate", 125, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Current, "run-current");
    }
}
