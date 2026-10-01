using System.Text.Json;
using Microsoft.Extensions.AI;
using PerformanceAgent.Core.Analysis;
using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;

namespace PerformanceAgent.AI.Tests;

/// <summary>Offline test seam: records what the provider sends and returns a scripted response. Never calls OpenAI.</summary>
internal sealed class FakeChatClient(
    Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    public int CallCount { get; private set; }
    public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
    public ChatOptions? Options { get; private set; }
    public CancellationToken Token { get; private set; }
    public bool Disposed { get; private set; }

    public static FakeChatClient Returning(string text) =>
        new((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));

    public static FakeChatClient Throwing(Exception exception) =>
        new((_, _, _) => Task.FromException<ChatResponse>(exception));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Messages = messages.ToArray();
        Options = options;
        Token = cancellationToken;
        return respond(Messages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Analysis does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Disposed = true;
}

internal static class TestData
{
    public const string Model = "configured-test-model";

    public const string ValidOutput = """
        {
          "summary": "Allocation increased.",
          "evidenceReferences": [{ "benchmarkName": "Sample.Work", "observation": "Allocated 64 -> 128 B/op." }],
          "hypotheses": [{ "statement": "An intermediate allocation may have been introduced.", "rationale": "Allocation doubled." }],
          "suggestedExperiments": [{ "description": "Remove the allocation and re-run the benchmark.", "expectedSignal": "Allocated B/op decreases." }],
          "uncertainty": "Source code was not supplied."
        }
        """;

    public static PerformanceAnalysisRequest Request()
    {
        var environment = new BenchmarkEnvironment(".NET 10.0.0", "Linux", "X64");
        return new PerformanceAnalysisRequest(
            new BenchmarkEvidence("1.0", new[] { new BenchmarkMeasurement("Sample.Work", 100, 64) }, environment),
            new BenchmarkEvidence("1.0", new[] { new BenchmarkMeasurement("Sample.Work", 125, 128) }, environment),
            new PerformanceBudget(10, 10),
            new[] { new PerformanceRegressionResult("Sample.Work", Passed: false, MeanExceeded: true, AllocationExceeded: true) });
    }

    public static OpenAIPerformanceAnalysisProvider Provider(IChatClient chatClient, TimeSpan? timeout = null) =>
        new(chatClient, Model, timeout ?? OpenAIPerformanceAnalysisProvider.DefaultTimeout);

    public static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
