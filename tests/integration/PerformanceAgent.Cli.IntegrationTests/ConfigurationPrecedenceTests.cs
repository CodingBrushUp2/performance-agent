using PerformanceAgent.Core.Budgets;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class ConfigurationPrecedenceTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly string _userConfigurationPath;

    public ConfigurationPrecedenceTests()
    {
        _userConfigurationPath = Path.Combine(_workspace.Directory, "user-config.json");
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Built_in_defaults_apply_when_no_configuration_exists()
    {
        var effective = Configuration().Inspect();

        Assert.Equal(new PerformanceBudget(5, 5), effective.Budget);
        Assert.Equal("Built-in defaults (file absent)", effective.BudgetSource);
        Assert.Equal("openai", effective.AiProvider);
        Assert.Equal("Built-in default", effective.AiProviderSource);
        Assert.Null(effective.AiModel);
        Assert.Equal("Not configured", effective.AiModelSource);
    }

    [Fact]
    public void User_configuration_supplies_ai_without_becoming_project_policy()
    {
        File.WriteAllText(_userConfigurationPath, """
            {
              "ai": { "provider": "OpenAI", "model": "global-model" },
              "budget": { "maxMeanRegressionPercent": 99 }
            }
            """);

        var effective = Configuration().Inspect();

        Assert.Equal(new PerformanceBudget(5, 5), effective.Budget);
        Assert.Equal("Built-in defaults (file absent)", effective.BudgetSource);
        Assert.Equal("OpenAI", effective.AiProvider);
        Assert.Equal(_userConfigurationPath, effective.AiProviderSource);
        Assert.Equal("global-model", effective.AiModel);
        Assert.Equal(_userConfigurationPath, effective.AiModelSource);
    }

    [Fact]
    public void Workspace_ai_overrides_user_ai_per_setting_and_workspace_budget_remains_project_policy()
    {
        File.WriteAllText(_userConfigurationPath, """
            { "ai": { "provider": "openai", "model": "global-model" } }
            """);
        _workspace.WriteConfiguration(new
        {
            budget = new { maxMeanRegressionPercent = 12.5, maxAllocationRegressionPercent = 20 },
            ai = new { model = "workspace-model" }
        });

        var effective = Configuration().Inspect();

        Assert.Equal(new PerformanceBudget(12.5, 20), effective.Budget);
        Assert.Equal("perfagent.json", effective.BudgetSource);
        Assert.Equal("openai", effective.AiProvider);
        Assert.Equal(_userConfigurationPath, effective.AiProviderSource);
        Assert.Equal("workspace-model", effective.AiModel);
        Assert.Equal(Path.Combine(_workspace.Directory, "perfagent.json"), effective.AiModelSource);
    }

    [Fact]
    public void Deterministic_budget_loading_ignores_malformed_user_ai_configuration()
    {
        File.WriteAllText(_userConfigurationPath, "{ broken user ai config");
        _workspace.WriteConfiguration(new
        {
            budget = new { maxMeanRegressionPercent = 7, maxAllocationRegressionPercent = 9 }
        });

        var effective = Configuration().InspectBudget();

        Assert.Equal(new PerformanceBudget(7, 9), effective.Budget);
        Assert.Equal("perfagent.json", effective.BudgetSource);
    }

    [Fact]
    public void Malformed_user_configuration_fails_without_echoing_contents()
    {
        const string secretLikeValue = "sk-must-not-echo";
        File.WriteAllText(_userConfigurationPath, "{ \"ai\": \"" + secretLikeValue + "\"");

        var exception = Assert.Throws<InvalidOperationException>(() => Configuration().Inspect());

        Assert.Contains("User Performance Agent configuration is not valid JSON", exception.Message, StringComparison.Ordinal);
        Assert.Contains(_userConfigurationPath, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretLikeValue, exception.Message, StringComparison.Ordinal);
    }

    private WorkspaceConfiguration Configuration() => new(_workspace.Storage, _userConfigurationPath);
}
