using System.Net;
using PerformanceAgent.Core.History;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class ReleasePolishUiTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Dashboard_hides_redundant_baseline_actions_and_links_help()
    {
        await _workspace.ArchiveAsync("run-current", 100, 64);
        await _workspace.ArchiveAsync("run-other", 101, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Current, "run-current");
        await _workspace.SetBaselineAsync(BaselineKind.Anchor, "run-current");

        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));
        var (status, page, _) = await ui.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Help / Getting Started", page, StringComparison.Ordinal);
        Assert.Contains("AI proposes. Measurements decide.", page, StringComparison.Ordinal);

        var currentRowStart = page.IndexOf("<code>run-current</code>", StringComparison.Ordinal);
        var currentRowEnd = page.IndexOf("</tr>", currentRowStart, StringComparison.Ordinal);
        var currentRow = page[currentRowStart..currentRowEnd];
        Assert.Contains("Current", currentRow, StringComparison.Ordinal);
        Assert.Contains("Anchor", currentRow, StringComparison.Ordinal);
        Assert.DoesNotContain("Make Current", currentRow, StringComparison.Ordinal);
        Assert.DoesNotContain("Make Anchor", currentRow, StringComparison.Ordinal);

        var otherRowStart = page.IndexOf("<code>run-other</code>", StringComparison.Ordinal);
        var otherRowEnd = page.IndexOf("</tr>", otherRowStart, StringComparison.Ordinal);
        var otherRow = page[otherRowStart..otherRowEnd];
        Assert.Contains("Make Current", otherRow, StringComparison.Ordinal);
        Assert.Contains("Make Anchor", otherRow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_page_uses_the_same_command_guidance_as_cli_help()
    {
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));

        var (status, page, _) = await ui.GetAsync("/help");
        HelpContent.TryRender(["check", "--help"], out var checkHelp);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Help / Getting Started", page, StringComparison.Ordinal);
        Assert.Contains("perfagent run &lt;project.csproj&gt;", page, StringComparison.Ordinal);
        Assert.Contains("perfagent analyze &lt;candidate-run-id&gt;", page, StringComparison.Ordinal);
        Assert.Contains("Current", page, StringComparison.Ordinal);
        Assert.Contains("Anchor", page, StringComparison.Ordinal);
        Assert.Contains("Deterministic results are authoritative", page, StringComparison.Ordinal);
        Assert.Contains("AI analysis is optional", page, StringComparison.Ordinal);
        Assert.Contains("The candidate is an evidence JSON file.", checkHelp, StringComparison.Ordinal);
        Assert.Contains("not an archived candidate", checkHelp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_details_exposes_immediate_analysis_progress_state()
    {
        await _workspace.ArchiveAsync("run-current", 100, 64);
        await _workspace.ArchiveAsync("run-candidate", 125, 64);
        await _workspace.SetBaselineAsync(BaselineKind.Current, "run-current");
        await using var ui = await UiServer.StartAsync(_workspace, (_, _) => RecordingProvider.Returning(Analyses.Claiming("x")));

        var (status, page, _) = await ui.GetAsync("/runs/run-candidate");
        var (scriptStatus, script, scriptResponse) = await ui.GetAsync("/assets/ui.js");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("data-analysis-form", page, StringComparison.Ordinal);
        Assert.Contains("data-analysis-button", page, StringComparison.Ordinal);
        Assert.Contains("data-analysis-status hidden", page, StringComparison.Ordinal);
        Assert.Contains("Analyzing with the configured provider", page, StringComparison.Ordinal);
        Assert.Contains("src=\"/assets/ui.js\"", page, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, scriptStatus);
        Assert.Contains("text/javascript", scriptResponse.Content.Headers.ContentType?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("button.disabled = true", script, StringComparison.Ordinal);
        Assert.Contains("button.textContent = \"Analyzing...\"", script, StringComparison.Ordinal);
        Assert.Contains("form.dataset.submitting === \"true\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_help_is_scannable_and_command_specific()
    {
        Assert.True(HelpContent.TryRender(["--help"], out var general));
        Assert.True(HelpContent.TryRender(["run", "--help"], out var run));
        Assert.True(HelpContent.TryRender(["check", "--help"], out var check));
        Assert.True(HelpContent.TryRender(["analyze", "--help"], out var analyze));

        Assert.Contains("Commands:", general, StringComparison.Ordinal);
        Assert.Contains("Typical workflow:", general, StringComparison.Ordinal);
        Assert.DoesNotContain(" | perfagent ", general, StringComparison.Ordinal);
        Assert.Contains("perfagent run <benchmark.csproj>", run, StringComparison.Ordinal);
        Assert.Contains("--candidate <candidate.json>", check, StringComparison.Ordinal);
        Assert.Contains("archived baseline run, not an archived candidate", check, StringComparison.Ordinal);
        Assert.Contains("perfagent analyze <candidate-run-id>", analyze, StringComparison.Ordinal);
    }
}
