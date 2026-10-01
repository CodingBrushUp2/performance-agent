# AI configuration

Performance Agent keeps AI provider selection outside the deterministic Core.

Workspace `perfagent.json` may contain non-secret settings:

```json
{
  "ai": {
    "provider": "openai",
    "model": "your-model-name"
  }
}
```

`provider` defaults to `openai` for V0.2. The model is deliberately not hard-coded; configure a model before live analysis.

Future provider adapters use the same settings shape. Provider-specific credentials are never stored here.

## OpenAI credential

The OpenAI adapter (`src/PerformanceAgent.AI`) reads the API key only from the `OPENAI_API_KEY` environment variable, and only when analysis is requested. Deterministic commands (`run`, `check`, `report`, `baseline`, `config show`, ...) never read it.

The key is passed to the OpenAI client for authentication only. It is never written to `perfagent.json`, evidence, baseline history, reports, or prompts, and Performance Agent never logs it or includes it in its error messages. Provider error responses are not echoed, because they can contain credential fragments. `perfagent config show` continues to show only non-secret settings.

Analysis fails with a clear message if `ai.model` is missing or `OPENAI_API_KEY` is unset. There is no default model.

## Timeout

One OpenAI analysis request is bounded by a provider-level default of 90 seconds, including SDK retries. Caller cancellation is passed through to the chat client. A configurable timeout (for example `ai.timeoutSeconds`) is deferred; it is not part of `perfagent.json` yet.
