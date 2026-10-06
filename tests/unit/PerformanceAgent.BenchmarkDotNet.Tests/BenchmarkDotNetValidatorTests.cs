using System.Diagnostics;
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
    public void ManualStopwatchTiming_ProducesPA1002WithoutInvalidatingBenchmark()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(ManualTimingBenchmark));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Source == "PerformanceAgent.PA1002");

        Assert.Equal(BenchmarkValidationSeverity.Warning, warning.Severity);
        Assert.Equal(nameof(ManualTimingBenchmark.Work), warning.BenchmarkMethod);
        Assert.Contains("Stopwatch", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualTimingAfterBranchesAndLocals_IsStillDetected()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(BranchedManualTimingBenchmark));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Source == "PerformanceAgent.PA1002"
                          && diagnostic.BenchmarkMethod == nameof(BranchedManualTimingBenchmark.Work));
    }

    [Fact]
    public void NormalBenchmark_DoesNotProducePA1002()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(NonTrivialBenchmark));

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Source == "PerformanceAgent.PA1002");
    }

    [Fact]
    public void ForcedGcInsideBenchmark_ProducesPA1003WithoutInvalidatingBenchmark()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(ForcedGcBenchmark));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Source == "PerformanceAgent.PA1003");

        Assert.Equal(BenchmarkValidationSeverity.Warning, warning.Severity);
        Assert.Equal(nameof(ForcedGcBenchmark.Work), warning.BenchmarkMethod);
        Assert.Contains("GC.Collect()", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForcedGcInGlobalSetup_DoesNotProducePA1003()
    {
        var result = new BenchmarkDotNetValidator().Validate(typeof(SetupGcBenchmark));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Source == "PerformanceAgent.PA1003");
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

    public class BranchedManualTimingBenchmark
    {
        private int _value = 41;

        [Benchmark]
        public long Work()
        {
            var value = _value;
            if ((value & 1) == 0)
                value += 2;
            else
                value += 1;

            var start = Stopwatch.GetTimestamp();
            return Stopwatch.GetTimestamp() - start + value;
        }
    }

    public class ManualTimingBenchmark
    {
        private int _value = 41;

        [Benchmark]
        public long Work()
        {
            var start = Stopwatch.GetTimestamp();
            var value = _value + 1;
            return Stopwatch.GetTimestamp() - start + value;
        }
    }

    public class ForcedGcBenchmark
    {
        private int _value = 41;

        [Benchmark]
        public int Work()
        {
            GC.Collect();
            return _value + 1;
        }
    }

    public class SetupGcBenchmark
    {
        private int _value;

        [GlobalSetup]
        public void Setup()
        {
            GC.Collect();
            _value = 41;
        }

        [Benchmark]
        public int Work() => _value + 1;
    }

    // Deliberately invalid fixtures exercise runtime validation.
#pragma warning disable BDN1103
    public class PrivateBenchmark
    {
        [Benchmark]
        private int Work() => 42;
    }

#pragma warning restore BDN1103

#pragma warning disable BDN1400
    public class MissingArgumentsBenchmark
    {
        [Benchmark]
        public int Work(int value) => value;
    }
#pragma warning restore BDN1400
}
