# Product Specification v0.1

Status: .NET V0.1 release candidate

## Product

Performance Agent is an evidence-driven performance engineering tool for .NET developers and coding agents.

It helps answer questions such as:
- Is this implementation measurably faster?
- Did this change introduce a performance regression?
- What evidence supports the result?

The measurement system, not the LLM, is the source of truth.

## Product principles

1. AI is optional. The core product must remain useful without an LLM.
2. Measurements are deterministic inputs to analysis. AI never invents benchmark numbers.
3. Prefer established measurement tools over a custom benchmark framework.
4. Every conclusion must be traceable to collected evidence.
5. Local-first for V1. No account, server, database, or cloud dependency.
6. Keep integrations as adapters around the core engine.
7. Complement existing observability/APM platforms rather than replacing them.
8. Prefer evidence correlation, experimentation, and verification over rebuilding telemetry collection already provided by mature tools.

## V1 use cases

### Compare implementations

Compare two benchmarkable .NET implementations and report timing and allocation differences.

### Detect regression

Compare a candidate result with a baseline and determine whether the configured performance budget is exceeded.

### Produce portable evidence

Generate machine-readable JSON plus human-readable Markdown. HTML can be added without changing the core model.

## AI modes

### AI disabled

Input -> experiment definition -> BenchmarkDotNet -> evidence normalization -> validation -> comparison -> report.

This path must provide a complete, trustworthy result.

### AI enabled

AI may assist with:
- interpreting developer intent;
- proposing hypotheses;
- suggesting an experiment plan;
- explaining measured differences;
- suggesting follow-up measurements.

AI output is advisory and must reference measured evidence. It cannot override or fabricate measurements.

## V1 interface

Primary interface: CLI.

Initial command direction:

```text
perfagent compare
perfagent check
perfagent report
```

Exact arguments remain implementation details until the first vertical slice proves the workflow.

## V1 outputs

- JSON evidence/result document.
- Markdown report suitable for terminals, CI artifacts, and pull requests.
- Explicit environment and validation information.
- Clear distinction between measured facts and AI interpretation.

## Out of scope for V1

- hosted dashboard;
- user accounts;
- database;
- billing;
- distributed benchmark runners;
- VS Code or Visual Studio extension;
- GitHub App;
- GitLab/Jira integrations;
- custom benchmarking runtime;
- API/load testing;
- mandatory LLM dependency.

## Complementary observability role

Performance Agent is not intended to become another full monitoring or APM platform. Existing systems such as cloud monitoring, OpenTelemetry collectors, metrics/log platforms, and .NET diagnostics remain authoritative collectors for the signals they own.

The product's differentiated layer is performance engineering over normalized evidence: correlate signals from multiple providers, form testable hypotheses, run controlled experiments when possible, and verify whether a proposed change measurably improves the result.

Future runtime diagnostics may use `dotnet-monitor` as a provider rather than reimplementing its diagnostics capabilities. Container/sidecar deployment is a future execution surface for observing applications with appropriate isolation and access; it is not required by the V1 benchmark CLI.

Result publishing is also an adapter concern. Future publishers may send configured outcomes to systems such as Teams, Jira, Confluence, or email. Publishing must not alter measured evidence or baseline state, and baseline selection remains an explicit action.

## Future surfaces

The core may later be exposed through:
- MCP;
- Codex/agent skills;
- GitHub/GitLab CI;
- IDE extensions;
- managed cloud runners and historical baselines.

These are consumers of the same core engine, not separate implementations.

## V1 success criteria

V1 is successful when a developer can run one local workflow that compares baseline and candidate .NET performance, obtains a reproducible evidence report without AI, optionally enriches that report with AI analysis, and can inspect which measurements support every conclusion.
