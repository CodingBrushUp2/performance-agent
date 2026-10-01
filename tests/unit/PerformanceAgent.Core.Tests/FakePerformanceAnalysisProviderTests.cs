using Xunit;
using PerformanceAgent.Core.Analysis;

namespace PerformanceAgent.Core.Tests;

public sealed class FakePerformanceAnalysisProviderTests
{
    [Fact]
    public async Task Same_request_produces_identical_output()
    {
        var provider = new FakePerformanceAnalysisProvider();

        var first = await provider.AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None);
        var second = await provider.AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None);

        Assert.Equal(AnalysisTestData.Json(first), AnalysisTestData.Json(second));
    }

    [Fact]
    public async Task Output_is_labelled_as_fake_and_not_ai_generated()
    {
        var analysis = await new FakePerformanceAnalysisProvider()
            .AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None);

        Assert.StartsWith(FakePerformanceAnalysisProvider.Label, analysis.Summary, StringComparison.Ordinal);
        Assert.Contains("no AI model was called", analysis.Summary, StringComparison.Ordinal);
        Assert.Contains("not AI analysis", analysis.Uncertainty, StringComparison.Ordinal);
        Assert.All(analysis.Hypotheses, x => Assert.StartsWith(FakePerformanceAnalysisProvider.Label, x.Statement, StringComparison.Ordinal));
        Assert.All(analysis.SuggestedExperiments, x => Assert.StartsWith(FakePerformanceAnalysisProvider.Label, x.Description, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Evidence_references_restate_measured_values_in_stable_order()
    {
        var analysis = await new FakePerformanceAnalysisProvider()
            .AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None);

        Assert.Collection(
            analysis.EvidenceReferences,
            x =>
            {
                Assert.Equal("Sample.Other", x.BenchmarkName);
                Assert.Equal("Mean 50 ns -> 51 ns; allocated 0 B/op -> 0 B/op.", x.Observation);
            },
            x =>
            {
                Assert.Equal("Sample.Work", x.BenchmarkName);
                Assert.Equal("Mean 100 ns -> 125 ns; allocated 64 B/op -> 128 B/op.", x.Observation);
            });
    }

    [Fact]
    public async Task Reports_deterministic_verdicts_without_changing_them()
    {
        var request = AnalysisTestData.Request();
        var before = AnalysisTestData.Json(request);

        var analysis = await new FakePerformanceAnalysisProvider().AnalyzeAsync(request, CancellationToken.None);

        Assert.Contains("1 of 2 exceeded the deterministic budget", analysis.Summary, StringComparison.Ordinal);
        Assert.Contains("Sample.Work", Assert.Single(analysis.Hypotheses).Statement, StringComparison.Ordinal);
        Assert.Contains("Sample.Work", Assert.Single(analysis.SuggestedExperiments).Description, StringComparison.Ordinal);
        Assert.Equal(before, AnalysisTestData.Json(request));
    }

    [Fact]
    public async Task States_when_regression_context_is_absent()
    {
        var request = AnalysisTestData.Request() with { RegressionResults = null };

        var analysis = await new FakePerformanceAnalysisProvider().AnalyzeAsync(request, CancellationToken.None);

        Assert.Contains("no deterministic regression context was supplied", analysis.Summary, StringComparison.Ordinal);
        Assert.Empty(analysis.Hypotheses);
        Assert.Empty(analysis.SuggestedExperiments);
    }

    [Fact]
    public async Task Output_passes_orchestration_validation()
    {
        var analysis = await new PerformanceAnalysisService(new FakePerformanceAnalysisProvider())
            .AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None);

        Assert.Equal(2, analysis.EvidenceReferences.Count);
    }

    [Fact]
    public async Task Honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new FakePerformanceAnalysisProvider().AnalyzeAsync(AnalysisTestData.Request(), cts.Token));
    }
}
