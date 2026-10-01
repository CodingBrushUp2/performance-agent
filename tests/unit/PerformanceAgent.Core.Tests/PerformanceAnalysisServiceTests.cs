using System.Text.Json;
using Xunit;
using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Tests;

public sealed class PerformanceAnalysisServiceTests
{
    [Fact]
    public async Task Analyze_returns_validated_structured_result_from_provider()
    {
        var expected = AnalysisTestData.ValidAnalysis();
        var provider = new StubProvider((_, _) => Task.FromResult(expected));

        var result = await new PerformanceAnalysisService(provider)
            .AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Equal("Sample.Work", Assert.Single(result.EvidenceReferences).BenchmarkName);
        Assert.Single(result.Hypotheses);
        Assert.Single(result.SuggestedExperiments);
    }

    [Fact]
    public async Task Analyze_calls_provider_exactly_once_with_request_content()
    {
        var request = AnalysisTestData.Request();
        var provider = new StubProvider((_, _) => Task.FromResult(AnalysisTestData.ValidAnalysis()));

        await new PerformanceAnalysisService(provider).AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(AnalysisTestData.Json(request), AnalysisTestData.Json(provider.LastRequest!));
    }

    [Fact]
    public void Constructor_requires_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new PerformanceAnalysisService(null!));
    }

    [Fact]
    public async Task Analyze_rejects_null_request_without_calling_provider()
    {
        var provider = new StubProvider((_, _) => Task.FromResult(AnalysisTestData.ValidAnalysis()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(null!, CancellationToken.None));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Analyze_rejects_invalid_request_without_calling_provider()
    {
        var request = AnalysisTestData.Request() with
        {
            Candidate = new BenchmarkEvidence("1.0", Array.Empty<BenchmarkMeasurement>()),
            RegressionResults = null,
        };
        var provider = new StubProvider((_, _) => Task.FromResult(AnalysisTestData.ValidAnalysis()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(request, CancellationToken.None));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Analyze_rejects_null_provider_output()
    {
        var provider = new StubProvider((_, _) => Task.FromResult<PerformanceAnalysis>(null!));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None));
    }

    public static TheoryData<string> MalformedOutputs => new()
    {
        "missing-summary",
        "null-evidence-reference",
        "blank-observation",
        "blank-hypothesis",
        "blank-experiment",
        "unknown-benchmark",
    };

    [Theory]
    [MemberData(nameof(MalformedOutputs))]
    public async Task Analyze_rejects_malformed_provider_output(string kind)
    {
        var valid = AnalysisTestData.ValidAnalysis();
        var malformed = kind switch
        {
            "missing-summary" => valid with { Summary = " " },
            "null-evidence-reference" => valid with { EvidenceReferences = [null!] },
            "blank-observation" => valid with { EvidenceReferences = [new("Sample.Work", "")] },
            "blank-hypothesis" => valid with { Hypotheses = [new(" ")] },
            "blank-experiment" => valid with { SuggestedExperiments = [new("")] },
            "unknown-benchmark" => valid with { EvidenceReferences = [new("Invented.Benchmark", "Made-up number.")] },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var provider = new StubProvider((_, _) => Task.FromResult(malformed));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Analyze_rejects_provider_output_with_missing_collections()
    {
        var malformed = AnalysisTestData.ValidAnalysis() with { Hypotheses = null! };
        var provider = new StubProvider((_, _) => Task.FromResult(malformed));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(AnalysisTestData.Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Provider_exception_propagates_unchanged_and_leaves_evidence_and_verdicts_untouched()
    {
        var request = AnalysisTestData.Request();
        var before = AnalysisTestData.Json(request);
        var verdictsBefore = request.RegressionResults!.Select(x => x.Passed).ToArray();
        var failure = new HttpRequestException("provider unavailable");
        var provider = new StubProvider((_, _) => Task.FromException<PerformanceAnalysis>(failure));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(request, CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(before, AnalysisTestData.Json(request));
        Assert.Equal(verdictsBefore, request.RegressionResults!.Select(x => x.Passed));
    }

    [Fact]
    public async Task Provider_cannot_mutate_caller_owned_evidence_or_regression_results()
    {
        var request = AnalysisTestData.Request();
        var candidateMeasurements = (BenchmarkMeasurement[])request.Candidate.Measurements;
        var originalMeasurement = candidateMeasurements[0];
        var originalResult = request.RegressionResults![0];
        var provider = new StubProvider((received, _) =>
        {
            TryOverwrite(received.Candidate.Measurements, new BenchmarkMeasurement("Sample.Work", 1, 1));
            TryOverwrite(received.RegressionResults!, received.RegressionResults![0] with { MeanExceeded = false, AllocationExceeded = false });
            return Task.FromResult(AnalysisTestData.ValidAnalysis());
        });

        await new PerformanceAnalysisService(provider).AnalyzeAsync(request, CancellationToken.None);

        Assert.Same(originalMeasurement, candidateMeasurements[0]);
        Assert.Same(originalResult, request.RegressionResults[0]);
        Assert.False(request.RegressionResults[0].Passed);
    }

    [Fact]
    public async Task Analyze_passes_caller_cancellation_token_to_provider()
    {
        using var cts = new CancellationTokenSource();
        var provider = new StubProvider((_, _) => Task.FromResult(AnalysisTestData.ValidAnalysis()));

        await new PerformanceAnalysisService(provider).AnalyzeAsync(AnalysisTestData.Request(), cts.Token);

        Assert.Equal(cts.Token, provider.LastToken);
    }

    [Fact]
    public async Task Analyze_with_already_cancelled_token_does_not_call_provider()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = new StubProvider((_, _) => Task.FromResult(AnalysisTestData.ValidAnalysis()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PerformanceAnalysisService(provider).AnalyzeAsync(AnalysisTestData.Request(), cts.Token));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Cancellation_during_provider_call_propagates_and_leaves_request_untouched()
    {
        using var cts = new CancellationTokenSource();
        var request = AnalysisTestData.Request();
        var before = AnalysisTestData.Json(request);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new StubProvider(async (_, token) =>
        {
            started.SetResult();
            // Bounded so a token-dropping implementation fails the test instead of hanging the run.
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return AnalysisTestData.ValidAnalysis();
        });

        var analysis = new PerformanceAnalysisService(provider).AnalyzeAsync(request, cts.Token);
        await started.Task;
        cts.Cancel();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis);
        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.Equal(before, AnalysisTestData.Json(request));
    }

    [Fact]
    public void Core_does_not_reference_ai_provider_sdks()
    {
        var forbidden = new[] { "OpenAI", "Azure.AI", "Microsoft.Extensions.AI", "Microsoft.SemanticKernel", "LangChain" };

        var references = typeof(PerformanceAnalysisService).Assembly.GetReferencedAssemblies().Select(x => x.Name ?? "");

        Assert.DoesNotContain(references, name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private static void TryOverwrite<T>(IReadOnlyList<T> list, T value)
    {
        try
        {
            if (list is IList<T> writable)
                writable[0] = value;
        }
        catch (NotSupportedException)
        {
        }
    }

    private sealed class StubProvider(
        Func<PerformanceAnalysisRequest, CancellationToken, Task<PerformanceAnalysis>> analyze)
        : IPerformanceAnalysisProvider
    {
        public int CallCount { get; private set; }
        public PerformanceAnalysisRequest? LastRequest { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<PerformanceAnalysis> AnalyzeAsync(PerformanceAnalysisRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastToken = cancellationToken;
            return analyze(request, cancellationToken);
        }
    }
}

internal static class AnalysisTestData
{
    public static PerformanceAnalysisRequest Request()
    {
        var baseline = new[]
        {
            new BenchmarkMeasurement("Sample.Other", 50, 0),
            new BenchmarkMeasurement("Sample.Work", 100, 64),
        };
        var candidate = new[]
        {
            new BenchmarkMeasurement("Sample.Other", 51, 0),
            new BenchmarkMeasurement("Sample.Work", 125, 128),
        };
        var budget = new PerformanceBudget(10, 10);
        var checker = new PerformanceBudgetChecker();
        var results = new[]
        {
            checker.Check(baseline[1], candidate[1], budget),
            checker.Check(baseline[0], candidate[0], budget),
        };

        return new PerformanceAnalysisRequest(
            new BenchmarkEvidence("1.0", baseline),
            new BenchmarkEvidence("1.0", candidate),
            budget,
            results);
    }

    public static PerformanceAnalysis ValidAnalysis() => new(
        "Allocation increased.",
        [new("Sample.Work", "Allocation changed from 64 B/op to 128 B/op.")],
        [new("An intermediate allocation may have been introduced.", "Measured allocation doubled.")],
        [new("Remove the intermediate allocation and benchmark again.", "Allocated B/op should decrease.")],
        "Source-code context was not supplied.");

    public static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
