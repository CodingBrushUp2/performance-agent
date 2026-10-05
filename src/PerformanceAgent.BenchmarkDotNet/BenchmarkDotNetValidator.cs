using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace PerformanceAgent.BenchmarkDotNet;

public sealed class BenchmarkDotNetValidator
{
    public BenchmarkValidationResult Validate(Type benchmarkType)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);

        BenchmarkRunInfo runInfo;
        try
        {
            runInfo = BenchmarkConverter.TypeToBenchmarks(benchmarkType);
        }
        catch (InvalidBenchmarkDeclarationException exception)
        {
            return new BenchmarkValidationResult(
                false,
                [
                    new BenchmarkValidationDiagnostic(
                        "BenchmarkDeclaration",
                        BenchmarkValidationSeverity.Error,
                        benchmarkType.FullName ?? benchmarkType.Name,
                        null,
                        exception.Message)
                ]);
        }

        var diagnostics = new List<BenchmarkValidationDiagnostic>();
        var validationParameters = new ValidationParameters(runInfo.BenchmarksCases, runInfo.Config);
        foreach (var validator in runInfo.Config.GetValidators())
        {
            foreach (var error in validator.Validate(validationParameters))
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
