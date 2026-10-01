using Xunit;
using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Tests;

public sealed class PerformanceAnalysisTests
{
    [Fact]
    public void Request_validation_accepts_normalized_evidence()
    {
        var request = new PerformanceAnalysisRequest(
            Evidence(100, 64),
            Evidence(125, 128),
            new PerformanceBudget(10, 10));

        Assert.Same(request, request.Validate());
    }

    [Fact]
    public void Analysis_validation_rejects_missing_summary()
    {
        var analysis = new PerformanceAnalysis(
            " ",
            Array.Empty<AnalysisEvidenceReference>(),
            Array.Empty<PerformanceHypothesis>(),
            Array.Empty<SuggestedExperiment>(),
            null);

        var exception = Assert.Throws<InvalidOperationException>(() => analysis.Validate());

        Assert.Contains("summary", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analysis_validation_accepts_structured_advisory_output()
    {
        var analysis = new PerformanceAnalysis(
            "Allocation increased.",
            [new("Sample.Work", "Allocation changed from 64 B/op to 128 B/op.")],
            [new("An intermediate allocation may have been introduced.", "Measured allocation doubled.")],
            [new("Remove the intermediate allocation and benchmark again.", "Allocated B/op should decrease.")],
            "Source-code context was not supplied.");

        Assert.Same(analysis, analysis.Validate());
    }

    private static BenchmarkEvidence Evidence(double meanNanoseconds, long allocatedBytes) =>
        new("1.0", [new BenchmarkMeasurement("Sample.Work", meanNanoseconds, allocatedBytes)]);
}
