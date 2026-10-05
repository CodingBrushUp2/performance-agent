using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace PerformanceAgent.BenchmarkDotNet;

public sealed class BenchmarkDotNetValidator
{
    public async Task<BenchmarkValidationResult> ValidateAsync(
        Type benchmarkType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);
        cancellationToken.ThrowIfCancellationRequested();

        await using var runInfo = await BenchmarkConverter.TypeToBenchmarksAsync(
            benchmarkType,
            cancellationToken: cancellationToken);

        var diagnostics = new List<BenchmarkValidationDiagnostic>();

        foreach (var error in runInfo.DeclarationErrors)
        {
            diagnostics.Add(Map(
                source: "BenchmarkDeclaration",
                benchmarkType,
                error));
        }

        foreach (var validator in runInfo.Config.GetValidators())
        {
            cancellationToken.ThrowIfCancellationRequested();

            await foreach (var error in validator
                               .ValidateAsync(runInfo)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                diagnostics.Add(Map(
                    validator.GetType().Name,
                    benchmarkType,
                    error));
            }
        }

        var ordered = diagnostics
            .Distinct()
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.BenchmarkType, StringComparer.Ordinal)
            .ThenBy(x => x.BenchmarkMethod, StringComparer.Ordinal)
            .ThenBy(x => x.Message, StringComparer.Ordinal)
            .ToArray();

        return new BenchmarkValidationResult(
            ordered.All(x => x.Severity != BenchmarkValidationSeverity.Error),
            ordered);
    }

    private static BenchmarkValidationDiagnostic Map(
        string source,
        Type benchmarkType,
        ValidationError error) =>
        new(
            source,
            error.IsCritical ? BenchmarkValidationSeverity.Error : BenchmarkValidationSeverity.Warning,
            benchmarkType.FullName ?? benchmarkType.Name,
            error.BenchmarkCase?.Descriptor.WorkloadMethod.Name,
            error.Message);
}
