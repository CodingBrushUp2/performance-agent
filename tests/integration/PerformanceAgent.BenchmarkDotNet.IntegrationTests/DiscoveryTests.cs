using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Attributes;
using PerformanceAgent.BenchmarkDotNet;
using Xunit;

namespace PerformanceAgent.BenchmarkDotNet.IntegrationTests;

public sealed class DiscoveryTests
{
    [Fact]
    public void PartialTypeLoad_PreservesValidBenchmarksAndLoaderDetails()
    {
        var assembly = new TestAssembly(() => throw new ReflectionTypeLoadException(
            [typeof(ZBenchmark), null, typeof(ABenchmark)],
            [new FileNotFoundException("Missing dependency Widgets.dll"), new TypeLoadException("Broken base type")]));
        var result = new BenchmarkDotNetRunner().DiscoverBenchmarks(assembly);
        Assert.Equal(new[] { typeof(ABenchmark), typeof(ZBenchmark) }, result.BenchmarkTypes);
        Assert.Contains(result.Diagnostics, message => message.Contains("Widgets.dll", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, message => message.Contains("Broken base type", StringComparison.Ordinal));
        Assert.All(result.Diagnostics, message => Assert.Contains("Restore dependencies", message));
        Assert.Equal(result.Diagnostics.Order(StringComparer.Ordinal), result.Diagnostics);
    }

    [Fact]
    public void LegacyDiscovery_DoesNotSilentlyReturnPartialResults()
    {
        var assembly = new TestAssembly(() => throw new ReflectionTypeLoadException(
            [typeof(ABenchmark), null], [new FileNotFoundException("Widgets.dll")]));
        var error = Assert.Throws<InvalidOperationException>(() => new BenchmarkDotNetRunner().DiscoverBenchmarkTypes(assembly));
        Assert.Contains("Widgets.dll", error.Message);
        Assert.Contains("Restore dependencies", error.Message);
    }

    [Fact]
    public void MethodInspectionFailure_SkipsOnlyAffectedType()
    {
        var assembly = new TestAssembly(() => [new UnloadableMethods(), typeof(ABenchmark)]);
        var result = new BenchmarkDotNetRunner().DiscoverBenchmarks(assembly);
        Assert.Equal(typeof(ABenchmark), Assert.Single(result.BenchmarkTypes));
        Assert.Contains("MethodDependency.dll", Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void TotalLoadFailure_ReturnsActionableDiagnosticsWithoutBenchmarks()
    {
        var result = new BenchmarkDotNetRunner().DiscoverBenchmarks(
            new TestAssembly(() => throw new BadImageFormatException("Wrong architecture")));
        Assert.Empty(result.BenchmarkTypes);
        Assert.Contains("Wrong architecture", Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void PartialLoadWithoutLoaderExceptions_StillReportsFailure()
    {
        var result = new BenchmarkDotNetRunner().DiscoverBenchmarks(new TestAssembly(() =>
            throw new ReflectionTypeLoadException([null], [])));
        Assert.Empty(result.BenchmarkTypes);
        Assert.Contains("Discovery incomplete", Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void Discovery_DoesNotConstructUnrelatedAttributes()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AttributeIsolation"), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("Benchmarks").DefineType("AttributedBenchmark", TypeAttributes.Public);
        var method = builder.DefineMethod("Work", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var benchmarkAttribute = typeof(ABenchmark).GetMethod(nameof(ABenchmark.Work))!.CustomAttributes
            .Single(attribute => attribute.AttributeType == typeof(BenchmarkAttribute));
        method.SetCustomAttribute(new CustomAttributeBuilder(benchmarkAttribute.Constructor,
            benchmarkAttribute.ConstructorArguments.Select(argument => argument.Value).ToArray()));
        method.SetCustomAttribute(new CustomAttributeBuilder(typeof(ThrowingAttribute).GetConstructor(Type.EmptyTypes)!, []));
        method.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var type = builder.CreateType()!;
        var result = new BenchmarkDotNetRunner().DiscoverBenchmarks(new TestAssembly(() => [type]));
        Assert.Equal(type, Assert.Single(result.BenchmarkTypes));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UnexpectedErrors_AreNotMisclassifiedAsLoaderFailures()
    {
        Assert.Throws<InvalidOperationException>(() => new BenchmarkDotNetRunner().DiscoverBenchmarks(
            new TestAssembly(() => throw new InvalidOperationException("Unexpected"))));
    }

    private sealed class TestAssembly(Func<Type[]> getTypes) : Assembly
    {
        public override Type[] GetTypes() => getTypes();
    }

    private sealed class UnloadableMethods() : TypeDelegator(typeof(ZBenchmark))
    {
        public override MethodInfo[] GetMethods(BindingFlags bindingAttr) => throw new FileNotFoundException("MethodDependency.dll");
    }

    public class ABenchmark
    {
        [Benchmark]
        public int Work() => 1;
    }

    public class ZBenchmark
    {
        [Benchmark]
        public int Work() => 2;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ThrowingAttribute : Attribute
    {
        public ThrowingAttribute() => throw new InvalidOperationException("Attribute constructor must not run during discovery.");
    }
}
