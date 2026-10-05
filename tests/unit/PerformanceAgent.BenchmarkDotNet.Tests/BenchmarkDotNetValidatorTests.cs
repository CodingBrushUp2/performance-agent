using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;
using Xunit;

namespace PerformanceAgent.BenchmarkDotNet.Tests;

public sealed class BenchmarkDotNetValidatorTests
{
    [Fact]
    public void ValidBenchmark_HasNoCriticalDiagnostics()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(ValidBenchmark));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == BenchmarkValidationSeverity.Error);
    }

    [Fact]
    public void ConstantBenchmark_ProducesPerformanceAgentWarningWithoutInvalidatingBenchmark()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(ValidBenchmark));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Source == "PerformanceAgent.PA1001");

        Assert.Equal(BenchmarkValidationSeverity.Warning, warning.Severity);
        Assert.Equal(nameof(ValidBenchmark.Work), warning.BenchmarkMethod);
        Assert.Contains("constant", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonTrivialBenchmark_DoesNotProducePA1001()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(NonTrivialBenchmark));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Source == "PerformanceAgent.PA1001");
    }

    [Fact]
    public void PrivateBenchmarkMethod_IsReportedAsInvalid()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(PrivateBenchmark));

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Severity == BenchmarkValidationSeverity.Error
            && item.Message.Contains("Method must be public", StringComparison.Ordinal));

        Assert.Equal("BenchmarkDeclaration", diagnostic.Source);
        Assert.Equal(typeof(PrivateBenchmark).FullName, diagnostic.BenchmarkType);
    }

    [Fact]
    public void ParameterizedBenchmarkWithoutArguments_IsReportedAsInvalid()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(MissingArgumentsBenchmark));

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

    public class NonTrivialBenchmark
    {
        private int _value = 41;

        [Benchmark]
        public int Work() => _value + 1;
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
