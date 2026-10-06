# Architecture v0.2

> Historical design document. New feature development is paused as of 2026-10-06. See the [implemented features](../README.md#what-it-does) and [closeout checklist](maintenance/release-checklist.md) for current scope and publication status.

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


## Provider-oriented evolution

The benchmark adapter is the first evidence provider, not the boundary of the product. Future providers may normalize runtime diagnostics, metrics, logs, traces, or cloud/APM evidence into explicit contracts without introducing vendor types into Core.

The intended direction is:

```text
Evidence Providers                 Performance Agent                 Result Publishers
-----------------                 -----------------                 -----------------
BenchmarkDotNet ----\              normalize/correlate              /--> Teams
 dotnet-monitor -----+-----------> analyze/experiment/verify ------+---> Jira
OpenTelemetry -------+                                                +--> Confluence
Metrics / logs ------+                                                \--> Email
Cloud/APM tools -----/
```

This is a complementary performance-engineering layer. Providers remain responsible for collecting their native telemetry; Performance Agent should not duplicate mature APM, diagnostics, metrics, or logging platforms.

Provider contracts should stay capability-oriented rather than vendor-oriented. Concrete adapters may later implement concepts such as evidence acquisition and experiment execution. Do not force all sources into one lowest-common-denominator interface when their capabilities differ.

## Execution surfaces

Native CLI execution remains the V1 surface. Container and sidecar modes are future deployment options, particularly for runtime diagnostics where process/container isolation and diagnostic access matter. A sidecar may consume evidence from tools such as `dotnet-monitor`; Performance Agent does not need to replace those tools.

Execution surfaces must feed the same normalized evidence and analysis pipeline. Core must not depend on Docker, Kubernetes, or a specific cloud runtime.

## Result publishing

Future result publishing is outbound adapter behavior. Defaults may come from project configuration and commands may override destinations or notification conditions. Publisher failures must not rewrite benchmark evidence, baseline history, or measured outcomes. Credentials belong in secret providers/environment-specific configuration, never committed repository configuration.

Calibration and analysis may recommend candidate run IDs, but publishers and AI adapters must never implicitly promote Current or Anchor. Baseline mutation remains an explicit command/domain action.
