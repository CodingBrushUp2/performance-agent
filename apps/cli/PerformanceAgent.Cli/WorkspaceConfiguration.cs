using System.Text.Json;
using PerformanceAgent.Core.Budgets;

namespace PerformanceAgent.Cli;

internal sealed record AiConfiguration(
    string? Provider = null,
    string? Model = null);

internal sealed record PerformanceAgentConfiguration(
    PerformanceBudget? Budget = null,
    AiConfiguration? Ai = null)
{
    public static PerformanceAgentConfiguration Default { get; } =
        new(new PerformanceBudget(5, 5), new AiConfiguration("openai", null));
}

internal sealed record EffectiveWorkspaceConfiguration(
    string Path,
    string BudgetSource,
    PerformanceBudget Budget,
    string AiProvider,
    string? AiModel);

internal sealed class WorkspaceConfiguration
{
    private readonly WorkspaceStorage _storage;

    public WorkspaceConfiguration(WorkspaceStorage storage) => _storage = storage;

    public PerformanceAgentConfiguration Load()
    {
        var effective = Inspect();
        return new(effective.Budget, new AiConfiguration(effective.AiProvider, effective.AiModel));
    }

    public EffectiveWorkspaceConfiguration Inspect()
    {
        var path = Path.Combine(_storage.WorkspaceDirectory, "perfagent.json");
        if (!File.Exists(path))
            return new(path, "Built-in defaults (file absent)", PerformanceAgentConfiguration.Default.Budget!, "openai", null);

        PerformanceAgentConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<PerformanceAgentConfiguration>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            // Do not echo raw JSON or values from unknown fields, which may contain secrets.
            throw new InvalidOperationException("perfagent.json is not valid configuration JSON. Fix the file and retry.", exception);
        }

        if (configuration is null)
            throw new InvalidOperationException("perfagent.json must contain a JSON object.");

        var budget = configuration.Budget ?? PerformanceAgentConfiguration.Default.Budget!;
        ValidateBudget(budget);
        var provider = configuration.Ai?.Provider?.Trim();
        if (string.IsNullOrEmpty(provider)) provider = "openai";
        var model = configuration.Ai?.Model?.Trim();
        if (model is not null && model.Length == 0) model = null;
        return new(path, configuration.Budget is null ? "Built-in defaults (budget absent)" : "perfagent.json", budget, provider, model);
    }

    private static void ValidateBudget(PerformanceBudget budget)
    {
        ValidatePercentage(budget.MaxMeanRegressionPercent, "maxMeanRegressionPercent");
        ValidatePercentage(budget.MaxAllocationRegressionPercent, "maxAllocationRegressionPercent");
    }

    private static void ValidatePercentage(double? value, string name)
    {
        if (value is not null && (!double.IsFinite(value.Value) || value.Value < 0))
            throw new InvalidOperationException($"perfagent.json budget.{name} must be a non-negative finite percentage or null.");
    }
}
