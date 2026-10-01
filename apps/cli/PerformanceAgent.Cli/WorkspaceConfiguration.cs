using System.Text.Json;
using PerformanceAgent.Core.Budgets;

namespace PerformanceAgent.Cli;

internal sealed record PerformanceAgentConfiguration(
    PerformanceBudget? Budget = null)
{
    public static PerformanceAgentConfiguration Default { get; } =
        new(new PerformanceBudget(5, 5));
}

internal sealed class WorkspaceConfiguration
{
    private readonly WorkspaceStorage _storage;

    public WorkspaceConfiguration(WorkspaceStorage storage) => _storage = storage;

    public PerformanceAgentConfiguration Load()
    {
        var path = Path.Combine(_storage.WorkspaceDirectory, "perfagent.json");
        if (!File.Exists(path))
            return PerformanceAgentConfiguration.Default;

        var configuration = JsonSerializer.Deserialize<PerformanceAgentConfiguration>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (configuration is null)
            throw new InvalidOperationException("perfagent.json must contain a JSON object.");

        var budget = configuration.Budget ?? PerformanceAgentConfiguration.Default.Budget!;
        ValidateBudget(budget);
        return configuration with { Budget = budget };
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
