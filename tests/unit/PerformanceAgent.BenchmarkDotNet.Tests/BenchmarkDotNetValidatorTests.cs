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
    public void ConstantBenchmark_IsWarningButRemainsValid()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(ConstantBenchmark));

        Assert.True(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Source == "PerformanceAgentValidityGuard"
            && item.Severity == BenchmarkValidationSeverity.Warning);

        Assert.Equal(nameof(ConstantBenchmark.Work), diagnostic.BenchmarkMethod);
        Assert.Contains("trivial constant or no-op", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldBackedBenchmark_DoesNotTriggerTrivialBodyWarning()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(FieldBackedBenchmark));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Diagnostics,
            item => item.Source == "PerformanceAgentValidityGuard");
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

    public class ConstantBenchmark
    {
        [Benchmark]
        public int Work() => 42;
    }

    public class FieldBackedBenchmark
    {
        private int _value = 42;

        [Benchmark]
        public int Work() => _value;
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
