namespace PerformanceAgent.BenchmarkDotNet;

public enum BenchmarkValidationSeverity
{
    Warning,
    Error
}

public sealed record BenchmarkValidationDiagnostic(
    string Source,
    BenchmarkValidationSeverity Severity,
    string BenchmarkType,
    string? BenchmarkMethod,
    string Message);

public sealed record BenchmarkValidationResult(
    bool IsValid,
    IReadOnlyList<BenchmarkValidationDiagnostic> Diagnostics,
    IReadOnlyList<string> BenchmarkNames);
