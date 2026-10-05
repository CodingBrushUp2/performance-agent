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

internal sealed record UserPerformanceAgentConfiguration(
    AiConfiguration? Ai = null);

internal sealed record EffectiveWorkspaceConfiguration(
    string Path,
    string UserPath,
    string BudgetSource,
    PerformanceBudget Budget,
    string AiProvider,
    string AiProviderSource,
    string? AiModel,
    string AiModelSource);

internal sealed class WorkspaceConfiguration
{
    private readonly WorkspaceStorage _storage;
    private readonly string _userConfigurationPath;

    public WorkspaceConfiguration(WorkspaceStorage storage, string? userConfigurationPath = null)
    {
        _storage = storage;
        _userConfigurationPath = userConfigurationPath ?? storage.UserConfigurationPath ?? DefaultUserConfigurationPath();
    }

    public static string DefaultUserConfigurationPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".performance-agent", "config.json");
    }

    public PerformanceAgentConfiguration Load()
    {
        var effective = Inspect();
        return new(effective.Budget, new AiConfiguration(effective.AiProvider, effective.AiModel));
    }

    public EffectiveWorkspaceConfiguration Inspect()
    {
        var workspacePath = Path.Combine(_storage.WorkspaceDirectory, "perfagent.json");
        var user = ReadUserConfiguration(_userConfigurationPath);
        var workspace = ReadWorkspaceConfiguration(workspacePath);

        var budget = workspace?.Budget ?? PerformanceAgentConfiguration.Default.Budget!;
        ValidateBudget(budget);

        var workspaceProvider = Normalize(workspace?.Ai?.Provider);
        var userProvider = Normalize(user?.Ai?.Provider);
        var provider = workspaceProvider ?? userProvider ?? "openai";
        var providerSource = workspaceProvider is not null
            ? workspacePath
            : userProvider is not null
                ? _userConfigurationPath
                : "Built-in default";

        var workspaceModel = Normalize(workspace?.Ai?.Model);
        var userModel = Normalize(user?.Ai?.Model);
        var model = workspaceModel ?? userModel;
        var modelSource = workspaceModel is not null
            ? workspacePath
            : userModel is not null
                ? _userConfigurationPath
                : "Not configured";

        var budgetSource = workspace is null
            ? "Built-in defaults (file absent)"
            : workspace.Budget is null
                ? "Built-in defaults (budget absent)"
                : "perfagent.json";

        return new(
            workspacePath,
            _userConfigurationPath,
            budgetSource,
            budget,
            provider,
            providerSource,
            model,
            modelSource);
    }

    private static PerformanceAgentConfiguration? ReadWorkspaceConfiguration(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var configuration = JsonSerializer.Deserialize<PerformanceAgentConfiguration>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return configuration
                ?? throw new InvalidOperationException("perfagent.json must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            // Do not echo raw JSON or values from unknown fields, which may contain secrets.
            throw new InvalidOperationException("perfagent.json is not valid configuration JSON. Fix the file and retry.", exception);
        }
    }

    private static UserPerformanceAgentConfiguration? ReadUserConfiguration(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var configuration = JsonSerializer.Deserialize<UserPerformanceAgentConfiguration>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return configuration
                ?? throw new InvalidOperationException("User Performance Agent configuration must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            // Never echo user configuration contents: unknown fields could contain secrets.
            throw new InvalidOperationException(
                $"User Performance Agent configuration is not valid JSON: {path}. Fix the file and retry.",
                exception);
        }
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
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
