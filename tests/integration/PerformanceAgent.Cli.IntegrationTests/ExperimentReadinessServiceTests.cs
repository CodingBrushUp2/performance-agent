using PerformanceAgent.Cli;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class ExperimentReadinessServiceTests
{
    private static readonly BudgetReadiness Budget = new("Built-in defaults", 5, 5);

    [Fact]
    public void SingleCandidateValidBenchmarkAndBaseline_IsReadyWithUnverifiedAssumptions()
    {
        var result = ExperimentReadinessService.Evaluate(
            Discovery(Candidate("src/Work.cs", "Work.Run()")),
            target: null,
            ValidBenchmark(),
            "run-baseline",
            Budget);

        Assert.Equal(ExperimentReadinessStatus.ReadyWithUnverifiedAssumptions, result.Status);
        Assert.Equal("src/Work.cs::Work.Run()", result.SelectedTarget!.Key);
        Assert.Equal("unverified", result.CoverageStatus);
        Assert.Equal("unverified", result.BaselineCompatibilityStatus);
        Assert.Empty(result.Blockers);
        Assert.Contains(result.NextActions, action =>
            action.Contains("Confirm that the benchmark project actually exercises", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleCandidatesWithoutTarget_NeedsExplicitSelection()
    {
        var result = ExperimentReadinessService.Evaluate(
            Discovery(
                Candidate("src/First.cs", "First.Run()"),
                Candidate("src/Second.cs", "Second.Run()")),
            target: null,
            ValidBenchmark(),
            "run-baseline",
            Budget);

        Assert.Equal(ExperimentReadinessStatus.NeedsInput, result.Status);
        Assert.Null(result.SelectedTarget);
        Assert.Contains(result.Blockers, blocker =>
            blocker.Contains("--target", StringComparison.Ordinal));
        Assert.Contains(result.NextActions, action =>
            action.Contains("--target", StringComparison.Ordinal));
    }

    [Fact]
    public void FullTargetKey_SelectsAmbiguousMemberName()
    {
        var first = Candidate("src/First.cs", "Worker.Run()");
        var second = Candidate("src/Second.cs", "Worker.Run()");

        var result = ExperimentReadinessService.Evaluate(
            Discovery(first, second),
            target: ExperimentReadinessService.TargetKey(second),
            ValidBenchmark(),
            "run-baseline",
            Budget);

        Assert.Equal(ExperimentReadinessStatus.ReadyWithUnverifiedAssumptions, result.Status);
        Assert.Equal("src/Second.cs", result.SelectedTarget!.FilePath);
    }

    [Fact]
    public void InvalidBenchmarkAndMissingBaseline_AreIndependentBlockers()
    {
        var result = ExperimentReadinessService.Evaluate(
            Discovery(Candidate("src/Work.cs", "Work.Run()")),
            target: null,
            new BenchmarkReadiness("Benchmarks.csproj", false, 1, ["Invalid declaration"]),
            currentBaselineRunId: null,
            Budget);

        Assert.Equal(ExperimentReadinessStatus.NeedsInput, result.Status);
        Assert.Contains(result.Blockers, blocker =>
            blocker.Contains("structurally invalid", StringComparison.Ordinal));
        Assert.Contains(result.Blockers, blocker =>
            blocker.Contains("No Current baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void NoCandidates_NeedsInput()
    {
        var result = ExperimentReadinessService.Evaluate(
            Discovery(),
            target: null,
            ValidBenchmark(),
            "run-baseline",
            Budget);

        Assert.Equal(ExperimentReadinessStatus.NeedsInput, result.Status);
        Assert.Contains(result.Blockers, blocker =>
            blocker.Contains("No changed C# member candidates", StringComparison.Ordinal));
    }

    private static CandidateDiscoveryResult Discovery(params CandidateHint[] candidates) =>
        new("1.0", "main", "HEAD", 5, candidates);

    private static CandidateHint Candidate(string file, string member) =>
        new(file, member, "method", 10, 2, "2 changed line(s) overlap this member.");

    private static BenchmarkReadiness ValidBenchmark() =>
        new("Benchmarks.csproj", true, 1, []);
}
