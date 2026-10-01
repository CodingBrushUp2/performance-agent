# V0.2 AI analysis architecture

Status: implementation direction

## Goal

Add optional AI-assisted performance analysis without changing the deterministic measurement and regression engine.

The invariant remains:

> AI proposes; measurements decide.

A model may explain measured evidence, form hypotheses, and suggest experiments. It must not create benchmark measurements, change a measured regression outcome, or mutate Current/Anchor baseline state.

## First vertical slice

```text
perfagent analyze <candidate-run-id>
        |
        v
Archived candidate + selected baseline
        |
        v
RegressionCheckService
        |
        v
PerformanceAnalysisRequest
        |
        v
IPerformanceAnalysisProvider
        |
        v
PerformanceAnalysis
  - summary
  - evidence references
  - hypotheses
  - suggested experiments
  - uncertainty
```

The same application capability will later back the local Web UI. The CLI remains the primary/headless surface.

### Implemented: analysis orchestration

`PerformanceAnalysisService` (Core) is the use case behind the provider boundary:

1. validates `PerformanceAnalysisRequest` (normalized evidence, budget, and optional deterministic `RegressionResults`);
2. passes the provider read-only copies of the evidence and verdict collections, plus the caller's `CancellationToken`;
3. validates the returned `PerformanceAnalysis`, including that every evidence reference names a measured benchmark;
4. returns the validated result. Provider exceptions and cancellation propagate unchanged.

The service has no access to archives, baseline stores, or regression checks, so analysis cannot alter measured facts, baselines, or PASS/REGRESSION verdicts.

A deterministic `FakePerformanceAnalysisProvider` lives in the Core test project for offline testing. Its output is labelled as fake and states that no AI model was called.

### Implemented: OpenAI provider

`OpenAIPerformanceAnalysisProvider` in `src/PerformanceAgent.AI` implements `IPerformanceAnalysisProvider` with `Microsoft.Extensions.AI` `IChatClient`, backed by the official OpenAI SDK through `Microsoft.Extensions.AI.OpenAI`.

- Typed structured output (`GetResponseAsync<PerformanceAnalysis>`) sends a JSON schema for the existing `PerformanceAnalysis` contract. Output that does not parse or validate fails the analysis clearly.
- The model receives only fixed grounding instructions and the serialized `PerformanceAnalysisRequest` (normalized evidence, budget, deterministic verdicts). It never receives repository content, workspace configuration, or credentials.
- The model comes only from `ai.model`, and the credential only from `OPENAI_API_KEY` (see [AI configuration](ai-configuration.md)).
- Authentication, rate-limit, outage, network, and malformed-output failures surface as provider-neutral exceptions that say benchmark results are unaffected. Provider SDK types do not escape `PerformanceAgent.AI`.
- Normal CI uses a fake `IChatClient` and never calls OpenAI.

The `perfagent analyze` command is not implemented yet.

## Provider boundary

The product owns a small performance-analysis-specific boundary rather than exposing an LLM SDK to Core:

```csharp
public interface IPerformanceAnalysisProvider
{
    Task<PerformanceAnalysis> AnalyzeAsync(
        PerformanceAnalysisRequest request,
        CancellationToken cancellationToken);
}
```

The request contains normalized Performance Agent evidence and deterministic regression results. The response is structured product data, not provider-specific chat objects.

Provider SDK types must not cross this boundary.

## Provider strategy

For V0.2, prefer the standard .NET AI abstraction ecosystem rather than introducing an agent framework.

`Microsoft.Extensions.AI` provides `IChatClient`, provider portability, structured output helpers, telemetry/caching middleware, and testable abstractions. A concrete OpenAI adapter can sit behind the Performance Agent-specific provider boundary.

Do not add Semantic Kernel in this phase. Its agent/plugin/planning surface is larger than the first analysis use case requires. Reconsider it only when a concrete capability justifies it.

Do not implement a custom HTTP OpenAI client when a maintained SDK/adapter already satisfies the requirement.

## Dependency direction

```text
PerformanceAgent.Core
        ^
        |
application/use-case capabilities
        ^
        |
PerformanceAgent.AI
        |
IPerformanceAnalysisProvider
        |
Microsoft.Extensions.AI abstraction
        |
concrete provider adapter
```

Core must not reference OpenAI, Azure OpenAI, Semantic Kernel, or another provider SDK.

## Structured result

The first contract should remain deliberately small:

- `Summary`
- `EvidenceReferences`
- `Hypotheses`
- `SuggestedExperiments`
- `Uncertainty`

Evidence references identify measured facts already present in normalized evidence/regression output. Generated prose must be visibly distinguished from measured facts.

The exact DTO shape is established by tests in the implementation PR rather than by provider response types.

## Failure semantics

AI is optional.

Provider timeout, authentication failure, rate limiting, malformed structured output, or provider outage must:

- fail the `analyze` operation clearly;
- never invalidate an already successful benchmark/check;
- never alter archived evidence;
- never alter baseline history;
- never turn a deterministic PASS into REGRESSION or vice versa.

Cancellation must flow to the provider.

## Configuration and secrets

Recognized non-secret AI settings belong in the same workspace configuration capability used by CLI and UI, for example provider/model and future timeout options.

API keys and tokens must not be written to `perfagent.json`, evidence, reports, prompts, logs, or baseline history. Provider credentials come from environment/OS/external secret providers.

Configuration visibility must show effective non-secret settings without revealing credential values.

## Testing strategy

Before a live provider is required:

1. fake deterministic analysis provider;
2. request construction tests from archived normalized evidence;
3. structured result validation tests;
4. provider failure/cancellation tests;
5. CLI tests proving deterministic commands still work with no AI configuration.

Live-provider tests are opt-in integration tests and must not make normal CI depend on a paid API.

## Explicitly deferred

V0.2 does not require:

- RAG or vector databases;
- multi-agent orchestration;
- autonomous code modification;
- MCP;
- Grafana/Kibana integrations;
- Java/JMH;
- JFR/async-profiler;
- cloud state or database.

Those capabilities can consume the same evidence/analysis boundaries later when justified.
