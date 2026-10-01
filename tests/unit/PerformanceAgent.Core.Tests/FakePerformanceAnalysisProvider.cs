using System.Globalization;
using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.Core.Tests;

/// <summary>
/// Deterministic, offline stand-in for an AI analysis provider. It never calls a model; its output is
/// derived mechanically from the request and is explicitly labelled as fake.
/// </summary>
internal sealed class FakePerformanceAnalysisProvider : IPerformanceAnalysisProvider
{
    public const string Label = "[fake provider]";

    public Task<PerformanceAnalysis> AnalyzeAsync(
        PerformanceAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var baseline = request.Baseline.Measurements.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var references = request.Candidate.Measurements
            .Where(x => baseline.ContainsKey(x.Name))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new AnalysisEvidenceReference(x.Name, Describe(baseline[x.Name], x)))
            .ToArray();

        var exceeded = (request.RegressionResults ?? [])
            .Where(x => !x.Passed)
            .Select(x => x.BenchmarkName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var verdict = request.RegressionResults is null
            ? "no deterministic regression context was supplied"
            : $"{exceeded.Length} of {request.RegressionResults.Count} exceeded the deterministic budget";

        return Task.FromResult(new PerformanceAnalysis(
            $"{Label} Deterministic analysis; no AI model was called. {references.Length} benchmark(s) compared; {verdict}.",
            references,
            exceeded.Select(name => new PerformanceHypothesis(
                    $"{Label} {name} exceeded its budget in the deterministic check.",
                    "Restates the measured verdict; the fake provider forms no causal hypothesis."))
                .ToArray(),
            exceeded.Select(name => new SuggestedExperiment(
                    $"{Label} Re-run {name} to confirm the measured change.",
                    "The deterministic verdict should reproduce."))
                .ToArray(),
            $"{Label} Output is generated mechanically for offline testing and is not AI analysis."));
    }

    private static string Describe(BenchmarkMeasurement baseline, BenchmarkMeasurement candidate) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Mean {baseline.MeanNanoseconds} ns -> {candidate.MeanNanoseconds} ns; allocated {Bytes(baseline)} -> {Bytes(candidate)}.");

    private static string Bytes(BenchmarkMeasurement measurement) =>
        measurement.AllocatedBytesPerOperation is { } bytes
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B/op")
            : "n/a";
}
