# Architecture v0.2

## Repository strategy

Performance Agent is a monorepo. The core libraries and all delivery surfaces live together while remaining independently buildable and deployable.

A separate repository is not created merely because something has a different output format. JSON, Markdown, and future HTML are reporters over the same application model.

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

## Monorepo layout

```text
src/
  PerformanceAgent.Core/
  PerformanceAgent.BenchmarkDotNet/
  PerformanceAgent.AI/

apps/
  cli/

integrations/
  mcp/                 # future
  codex/               # future
  github/              # future
  vscode/              # future

tests/
  unit/
  integration/
  e2e/

samples/
docs/

PerformanceAgent.sln
```

Only directories needed by the current milestone should be materialized. Future directories in this document describe boundaries, not an instruction to create empty scaffolding.

## Core boundaries

Core owns:
- experiment definitions;
- normalized measurements;
- environment evidence;
- validation findings;
- comparisons;
- performance budgets;
- report model and reporter contracts.

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

Integrations own protocol-specific translation only. MCP, Codex skills, GitHub, and IDE integrations must not duplicate performance-analysis business logic.

## Hard constraints

- Core must not reference an LLM SDK.
- Core must not reference BenchmarkDotNet types.
- AI analysis must accept normalized evidence rather than raw benchmark implementation objects.
- Secrets must not be stored in repository configuration.
- A failed AI request must not invalidate a successful benchmark result.
- Reports must label measured facts separately from generated interpretation.
- Delivery surfaces may depend on Core; Core must never depend on a delivery surface.
- Output formats must not become separate repositories solely because they are separate formats.

## Split criteria

A component should move to a separate repository only if there is a concrete operational reason, such as an independent ownership model, incompatible release lifecycle, security boundary, or technology/tooling constraint. Repository splitting is not part of V1.


## Run history and baseline provenance

Benchmark evidence is immutable once archived. A baseline is a reference to an archived run, not a mutable copy of benchmark measurements.

Baseline changes are append-only events. The history distinguishes an **anchor baseline** from the **current baseline** so resetting or promoting the current baseline does not erase the original performance reference.

A future storage adapter may persist the same model in files, object storage, SQLite, or PostgreSQL. Core history types remain storage-agnostic.

The initial file-backed layout is expected to follow this shape:

```text
.performance-agent/
├── archive/
│   └── <run-id>.json
├── baselines/
│   ├── anchor.json
│   └── current.json
└── baseline-events.jsonl
```

The archive is the source of truth. Baseline reference files are derived pointers for convenient lookup. A reset must create an auditable event containing the target run and previous run rather than overwriting history.
