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

Future provider adapters use the same settings shape. Provider-specific credentials are never stored here. OpenAI credentials will be read from a secret-safe external source such as an environment variable.
