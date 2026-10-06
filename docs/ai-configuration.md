# AI configuration

Performance Agent keeps AI provider selection outside the deterministic Core. AI configuration is non-secret and layered so a developer does not need to repeat the same provider/model in every benchmark workspace.

## Configuration layers

Effective settings use this precedence:

1. built-in defaults
2. user AI configuration
3. workspace `perfagent.json`
4. explicit CLI overrides where a command supports them

The default user configuration path is:

- Windows: `%USERPROFILE%\.performance-agent\config.json`
- Linux/macOS: `~/.performance-agent/config.json`

The user configuration is intended for non-secret AI defaults only:

```json
{
  "ai": {
    "provider": "openai",
    "model": "your-model-name"
  }
}
```

A workspace may override either AI setting in `perfagent.json`:

```json
{
  "ai": {
    "model": "workspace-specific-model"
  },
  "budget": {
    "maxMeanRegressionPercent": 5,
    "maxAllocationRegressionPercent": 10
  }
}
```

Performance budgets remain workspace/project policy. They are not read from the user AI configuration. This keeps team policy commit-able with the repository while personal/default provider choices stay outside it.

`provider` defaults to `openai` for V0.2 and is matched case-insensitively. Other values fail `perfagent analyze` with a message naming the configured provider. The model is deliberately not hard-coded; configure a model before live analysis.

Run `perfagent config show` or open **Effective configuration** in the Local Web UI to see the effective non-secret values and where each value came from.

Future provider adapters use the same settings shape. Provider-specific credentials are never stored in either configuration file.

## OpenAI credential

The OpenAI adapter (`src/PerformanceAgent.AI`) reads the API key only from the `OPENAI_API_KEY` environment variable, and only when analysis is requested (`perfagent analyze <candidate-run-id>`). Deterministic commands (`run`, `check`, `report`, `baseline`, `config show`, ...) never use the key for inference.

The key is passed to the OpenAI client for authentication only. It is never written to user configuration, `perfagent.json`, evidence, baseline history, reports, or prompts, and Performance Agent never logs it or includes it in its error messages. Provider error responses are not echoed, because they can contain credential fragments.

Analysis fails with a clear message if `ai.model` is missing or `OPENAI_API_KEY` is unset. There is no default model.

## Data sent to the provider

Requesting analysis sends the normalized baseline and candidate evidence, budget policy, and deterministic per-benchmark results to the configured OpenAI model. This includes benchmark names, measurement statistics, and runtime/OS/architecture metadata. Repository source, configuration files, and the API key are not prompt content. Benchmark names and other strings may still contain sensitive information supplied by the user; inspect evidence before using external analysis. The provider call may incur API charges.

## Timeout

One OpenAI analysis request is bounded by a provider-level default of 90 seconds, including SDK retries. Caller cancellation is passed through to the chat client. A configurable timeout (for example `ai.timeoutSeconds`) is deferred; it is not part of configuration yet.
