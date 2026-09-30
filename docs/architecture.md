# Architecture v0.1

## Dependency rule

The performance evidence pipeline is the product core. CLI, AI providers, benchmark tools, and future agent integrations are adapters.

```text
CLI / future MCP / future IDE
            |
            v
      Application Core
       /          \
      v            v
Experiment       Reporting
      |
      v
Measurement Abstraction
      |
      v
BenchmarkDotNet Adapter

Optional AI Adapter
      |
      v
Analysis Provider
```

## Proposed projects

```text
src/
  PerformanceAgent.Core/
  PerformanceAgent.BenchmarkDotNet/
  PerformanceAgent.Cli/
  PerformanceAgent.AI/

tests/
  PerformanceAgent.Core.Tests/
  PerformanceAgent.IntegrationTests/

samples/
docs/
integrations/
```

## Core boundaries

Core owns:
- experiment definitions;
- normalized measurements;
- environment evidence;
- validation findings;
- comparisons;
- performance budgets;
- report model.

BenchmarkDotNet adapter owns:
- BenchmarkDotNet-specific configuration;
- execution;
- translation into normalized evidence.

AI adapter owns:
- provider abstraction;
- prompts/contracts;
- conversion of evidence into advisory analysis.

CLI owns:
- argument parsing;
- orchestration entry points;
- exit codes;
- presentation.

## Hard constraints

- Core must not reference an LLM SDK.
- Core must not reference BenchmarkDotNet types.
- AI analysis must accept normalized evidence rather than raw benchmark implementation objects.
- Secrets must not be stored in repository configuration.
- A failed AI request must not invalidate a successful benchmark result.
- Reports must label measured facts separately from generated interpretation.
