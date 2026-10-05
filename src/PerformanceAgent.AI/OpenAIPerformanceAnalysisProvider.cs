using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using PerformanceAgent.Core.Analysis;

namespace PerformanceAgent.AI;

/// <summary>
/// Advisory OpenAI analysis behind <see cref="IPerformanceAnalysisProvider"/>, implemented with
/// Microsoft.Extensions.AI <see cref="IChatClient"/>. Provider SDK types never cross this class's public surface.
/// </summary>
public sealed class OpenAIPerformanceAnalysisProvider : IPerformanceAnalysisProvider, IDisposable
{
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    /// <summary>Upper bound for one interactive analysis request, including SDK retries.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions OutputJsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private readonly IChatClient _chatClient;
    private readonly string _model;
    private readonly TimeSpan _timeout;

    internal OpenAIPerformanceAnalysisProvider(IChatClient chatClient, string model, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _chatClient = chatClient;
        _model = model;
        _timeout = timeout;
    }

    internal IChatClient InnerChatClient => _chatClient;

    /// <summary>
    /// Creates the provider from the configured model and the <c>OPENAI_API_KEY</c> environment variable.
    /// Call only when analysis is requested; deterministic commands never need AI configuration.
    /// </summary>
    public static OpenAIPerformanceAnalysisProvider Create(string? model) =>
        Create(model, Environment.GetEnvironmentVariable, CreateOpenAIChatClient);

    internal static OpenAIPerformanceAnalysisProvider Create(
        string? model,
        Func<string, string?> getEnvironmentVariable,
        Func<string, string, IChatClient> createChatClient)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(
                "AI analysis requires a model. Set \"ai.model\" in the user config or workspace perfagent.json; no default model is assumed.");

        var apiKey = getEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"AI analysis with provider 'openai' requires the {ApiKeyEnvironmentVariable} environment variable. " +
                "API keys are never read from Performance Agent configuration files.");

        model = model.Trim();
        return new(createChatClient(model, apiKey), model, DefaultTimeout);
    }

    public async Task<PerformanceAnalysis> AnalyzeAsync(
        PerformanceAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        ChatResponse<PerformanceAnalysis> response;
        try
        {
            response = await _chatClient.GetResponseAsync<PerformanceAnalysis>(
                AnalysisPrompt.CreateMessages(request),
                OutputJsonOptions,
                new ChatOptions { ModelId = _model },
                cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"OpenAI analysis did not complete within {_timeout.TotalSeconds:0} seconds. Benchmark results are unaffected.",
                exception);
        }
        catch (ClientResultException exception)
        {
            // Not chained: provider error bodies can echo credential fragments, and SDK types must not escape.
            throw new InvalidOperationException(DescribeFailure(exception.Status));
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(DescribeFailure(0));
        }

        if (!response.TryGetResult(out var analysis))
            throw Malformed("the response was not valid JSON for the analysis schema");

        try
        {
            return analysis.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw Malformed(exception.Message);
        }
    }

    public void Dispose() => _chatClient.Dispose();

    internal static string DescribeFailure(int status) => status switch
    {
        401 or 403 => $"OpenAI rejected the credential (HTTP {status}). Check the {ApiKeyEnvironmentVariable} environment variable.",
        404 => "OpenAI did not find the configured model (HTTP 404). Check \"ai.model\" in the user config or workspace perfagent.json.",
        429 => "OpenAI rate limit or quota exceeded (HTTP 429). Retry later.",
        400 => "OpenAI rejected the analysis request (HTTP 400). The configured model may not support structured output.",
        0 => "OpenAI could not be reached. Check network connectivity and retry.",
        >= 500 => $"OpenAI is unavailable (HTTP {status}). Retry later.",
        _ => $"OpenAI analysis request failed (HTTP {status}).",
    } + " Benchmark results are unaffected.";

    private static InvalidOperationException Malformed(string reason) =>
        new($"OpenAI returned malformed structured analysis: {reason}");

    internal static IChatClient CreateOpenAIChatClient(string model, string apiKey) =>
        new global::OpenAI.Chat.ChatClient(
                model,
                new ApiKeyCredential(apiKey),
                new global::OpenAI.OpenAIClientOptions { NetworkTimeout = DefaultTimeout })
            .AsIChatClient();
}
