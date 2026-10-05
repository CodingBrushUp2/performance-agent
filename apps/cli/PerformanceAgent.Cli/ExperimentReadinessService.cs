using System.Text.Json;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.History;

namespace PerformanceAgent.Cli;

internal enum ExperimentReadinessStatus
{
    NeedsInput,
    ReadyWithUnverifiedCoverage
}

internal sealed record ExperimentTarget(
    string Key,
    string FilePath,
    string Member,
    string Kind,
    int StartLine,
    int ChangedLines,
    string Reason);

internal sealed record BenchmarkReadiness(
    string ProjectPath,
    bool Valid,
    int BenchmarkTypeCount,
    IReadOnlyList<string> Diagnostics);

internal sealed record BudgetReadiness(
    string Source,
    double? MaxMeanRegressionPercent,
    double? MaxAllocationRegressionPercent);

internal sealed record ExperimentReadinessResult(
    string SchemaVersion,
    ExperimentReadinessStatus Status,
    string BaseRef,
    string HeadRef,
    IReadOnlyList<ExperimentTarget> Candidates,
    ExperimentTarget? SelectedTarget,
    BenchmarkReadiness Benchmark,
    string? CurrentBaselineRunId,
    BudgetReadiness Budget,
    string CoverageStatus,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> NextActions);

internal sealed class ExperimentReadinessService
{
    private readonly GitDiffCandidateService _candidateService;
    private readonly ProjectRunner _projectRunner;
    private readonly WorkspaceStorage _storage;

    public ExperimentReadinessService(
        GitDiffCandidateService? candidateService = null,
        ProjectRunner? projectRunner = null,
        WorkspaceStorage? storage = null)
    {
        _candidateService = candidateService ?? new GitDiffCandidateService();
        _projectRunner = projectRunner ?? new ProjectRunner();
        _storage = storage ?? WorkspaceStorage.Resolve();
    }

    public async Task<ExperimentReadinessResult> InspectAsync(
        string baseRef,
        string benchmarkProject,
        string headRef = "HEAD",
        int limit = 5,
        string? target = null,
        bool workingTree = false,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _candidateService.DiscoverAsync(
            baseRef,
            headRef,
            limit,
            cancellationToken,
            workingTree: workingTree);

        var validation = await _projectRunner.ValidateAsync(
            benchmarkProject,
            cancellationToken);

        if (validation.ExitCode == 2 || string.IsNullOrWhiteSpace(validation.Validation))
        {
            var details = string.IsNullOrWhiteSpace(validation.StandardError)
                ? validation.StandardOutput.Trim()
                : validation.StandardError.Trim();
            throw new InvalidOperationException(
                details.Length == 0
                    ? "Benchmark project validation could not be completed."
                    : $"Benchmark project validation could not be completed. {details}");
        }

        var benchmark = ParseBenchmarkReadiness(
            benchmarkProject,
            validation.Validation);

        var current = await new FileBaselineStore(_storage.StateDirectory)
            .GetAsync(BaselineKind.Current, cancellationToken);

        var budget = new WorkspaceConfiguration(_storage).InspectBudget();

        return Evaluate(
            candidates,
            target,
            benchmark,
            current?.RunId,
            new BudgetReadiness(
                budget.BudgetSource,
                budget.Budget.MaxMeanRegressionPercent,
                budget.Budget.MaxAllocationRegressionPercent));
    }

    internal static ExperimentReadinessResult Evaluate(
        CandidateDiscoveryResult discovery,
        string? target,
        BenchmarkReadiness benchmark,
        string? currentBaselineRunId,
        BudgetReadiness budget)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(budget);

        var candidates = discovery.Candidates
            .Select(candidate => new ExperimentTarget(
                TargetKey(candidate),
                candidate.FilePath,
                candidate.Member,
                candidate.Kind,
                candidate.StartLine,
                candidate.ChangedLines,
                candidate.Reason))
            .ToArray();

        var blockers = new List<string>();
        ExperimentTarget? selected = null;

        if (candidates.Length == 0)
        {
            blockers.Add("No changed C# member candidates were found in the selected diff.");
        }
        else if (!string.IsNullOrWhiteSpace(target))
        {
            var matches = candidates
                .Where(candidate =>
                    string.Equals(candidate.Key, target, StringComparison.Ordinal)
                    || string.Equals(candidate.Member, target, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 1)
                selected = matches[0];
            else if (matches.Length == 0)
                blockers.Add($"Target '{target}' was not found in the candidate hints.");
            else
                blockers.Add($"Target '{target}' is ambiguous; use the full file::member target key.");
        }
        else if (candidates.Length == 1)
        {
            selected = candidates[0];
        }
        else
        {
            blockers.Add("Multiple candidate hints exist; select one with --target <file::member>.");
        }

        if (!benchmark.Valid)
            blockers.Add("The benchmark project is structurally invalid. Fix validation errors before measuring.");

        if (string.IsNullOrWhiteSpace(currentBaselineRunId))
            blockers.Add("No Current baseline is selected.");

        var nextActions = BuildNextActions(
            blockers,
            candidates,
            selected,
            benchmark,
            currentBaselineRunId);

        return new ExperimentReadinessResult(
            "1.0",
            blockers.Count == 0
                ? ExperimentReadinessStatus.ReadyWithUnverifiedCoverage
                : ExperimentReadinessStatus.NeedsInput,
            discovery.BaseRef,
            discovery.HeadRef,
            candidates,
            selected,
            benchmark,
            currentBaselineRunId,
            budget,
            selected is null ? "not-assessed" : "unverified",
            blockers,
            nextActions);
    }

    internal static string TargetKey(CandidateHint candidate) =>
        $"{candidate.FilePath}::{candidate.Member}";

    private static BenchmarkReadiness ParseBenchmarkReadiness(
        string benchmarkProject,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var diagnostics = root.GetProperty("diagnostics")
            .EnumerateArray()
            .Select(item => item.GetProperty("message").GetString() ?? string.Empty)
            .Where(message => message.Length != 0)
            .ToArray();

        return new BenchmarkReadiness(
            Path.GetFullPath(benchmarkProject),
            root.GetProperty("valid").GetBoolean(),
            root.GetProperty("benchmarkTypeCount").GetInt32(),
            diagnostics);
    }

    private static IReadOnlyList<string> BuildNextActions(
        IReadOnlyCollection<string> blockers,
        IReadOnlyList<ExperimentTarget> candidates,
        ExperimentTarget? selected,
        BenchmarkReadiness benchmark,
        string? currentBaselineRunId)
    {
        var actions = new List<string>();

        if (candidates.Count != 0 && selected is null)
            actions.Add("Select one candidate target and rerun readiness with --target <file::member>.");

        if (!benchmark.Valid)
            actions.Add($"Run 'perfagent validate {benchmark.ProjectPath}' and fix the reported validation errors.");

        if (string.IsNullOrWhiteSpace(currentBaselineRunId))
            actions.Add($"Calibrate '{benchmark.ProjectPath}', then explicitly select a trusted Current baseline.");

        if (blockers.Count == 0)
        {
            actions.Add("Confirm that the benchmark project actually exercises the selected target; coverage is not verified by Performance Agent.");
            actions.Add($"Run 'perfagent run {benchmark.ProjectPath}', then check the archived candidate against Current.");
        }

        return actions;
    }
}
