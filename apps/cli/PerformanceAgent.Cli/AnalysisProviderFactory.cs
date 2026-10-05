using PerformanceAgent.AI;
using PerformanceAgent.Core.Analysis;

namespace PerformanceAgent.Cli;

/// <summary>
/// Resolves the configured <c>ai.provider</c> to an analysis provider. V0.2 supports only OpenAI;
/// add a case here when another adapter exists rather than introducing a plugin framework.
/// </summary>
internal static class AnalysisProviderFactory
{
    public const string SupportedProviders = "openai";

    public static IPerformanceAnalysisProvider Create(string provider, string? model) =>
        string.Equals(provider?.Trim(), "openai", StringComparison.OrdinalIgnoreCase)
            ? OpenAIPerformanceAnalysisProvider.Create(model)
            : throw new InvalidOperationException(
                $"AI provider '{provider}' is not supported. Set \"ai.provider\" in the user config or workspace perfagent.json to one of: {SupportedProviders}.");
}
