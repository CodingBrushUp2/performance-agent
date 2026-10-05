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

    [Fact]
    public void ForcedGcInsideBenchmark_IsReportedAsWarningButRemainsValid()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(ForcedGcBenchmark));

        Assert.True(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Source == "PerformanceAgent.MeasuredRegionHygiene"
            && item.Severity == BenchmarkValidationSeverity.Warning);

        Assert.Equal(nameof(ForcedGcBenchmark.Work), diagnostic.BenchmarkMethod);
        Assert.Contains("GC.Collect()", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForcedGcInGlobalSetup_IsNotReportedAsMeasuredRegionWarning()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(SetupGcBenchmark));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Diagnostics, item =>
            item.Source == "PerformanceAgent.MeasuredRegionHygiene");
    }

    public class ValidBenchmark
    {
        [Benchmark]
        public int Work() => 42;
    }

    public class ForcedGcBenchmark
    {
        [Benchmark]
        public int Work()
        {
            GC.Collect();
            return 42;
        }
    }

    public class SetupGcBenchmark
    {
        [GlobalSetup]
        public void Setup() => GC.Collect();

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
