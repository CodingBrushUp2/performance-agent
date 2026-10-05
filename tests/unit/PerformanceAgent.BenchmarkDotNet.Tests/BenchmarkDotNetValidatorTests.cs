using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;
using Xunit;

namespace PerformanceAgent.BenchmarkDotNet.Tests;

public sealed class BenchmarkDotNetValidatorTests
{
    [Fact]
    public async Task ValidBenchmark_HasNoCriticalDiagnostics()
    {
        var result = await new BenchmarkDotNetValidator().ValidateAsync(typeof(ValidBenchmark));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == BenchmarkValidationSeverity.Error);
    }

    [Fact]
    public async Task PrivateBenchmarkMethod_IsReportedAsInvalid()
    {
        var result = await new BenchmarkDotNetValidator().ValidateAsync(typeof(PrivateBenchmark));

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Severity == BenchmarkValidationSeverity.Error
            && item.Message.Contains("Method must be public", StringComparison.Ordinal));

        Assert.Equal("BenchmarkDeclaration", diagnostic.Source);
        Assert.Equal(typeof(PrivateBenchmark).FullName, diagnostic.BenchmarkType);
    }

    [Fact]
    public async Task ParameterizedBenchmarkWithoutArguments_IsReportedAsInvalid()
    {
        var result = await new BenchmarkDotNetValidator().ValidateAsync(typeof(MissingArgumentsBenchmark));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item =>
            item.Severity == BenchmarkValidationSeverity.Error
            && item.Message.Contains("shouldn't have any arguments", StringComparison.OrdinalIgnoreCase));
    }

    public class ValidBenchmark
    {
        [Benchmark]
        public int Work() => 42;
    }

    public class PrivateBenchmark
    {
        [Benchmark]
        private int Work() => 42;
    }

    public class MissingArgumentsBenchmark
    {
        [Benchmark]
        public int Work(int value) => value;
    }
}
