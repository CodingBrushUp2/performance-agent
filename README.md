# Performance Agent

Evidence-driven performance engineering for .NET developers and coding agents.

Performance Agent measures first and explains second. Its core workflow works without an LLM; optional AI can help plan experiments and interpret evidence, but benchmark measurements remain the source of truth.

> Project status: early design / V1 bootstrap. The repository name is temporary and is not the final product brand.

## Runtime compatibility

Performance Agent 1.x starts on .NET 10 LTS and follows stable GA .NET releases. Preview and RC runtimes are not production baselines. Product versions are independent from .NET versions; supported runtime changes are tracked explicitly rather than encoded into the product major version.

Current baseline:
- Target framework: `net10.0`
- SDK line: .NET 10 stable, pinned by `global.json`
- BenchmarkDotNet: 0.15.8 stable

## V1 direction

- Local-first CLI
- BenchmarkDotNet as the first measurement adapter
- Baseline vs candidate comparison
- Regression/performance-budget checks
- JSON and Markdown evidence reports
- Optional AI analysis through a provider abstraction

See [Product Specification](docs/product-spec.md), [Architecture](docs/architecture.md), and [Security model](SECURITY.md).

## Local tool install

Until a public package is published, build and install the CLI from a local package:

```bash
dotnet pack apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj -c Release -o artifacts
dotnet tool install --global PerformanceAgent.Cli --version 0.1.0 --add-source ./artifacts
```

Then run:

```bash
perfagent run path/to/Benchmarks.csproj
perfagent check baseline.json candidate.json --budget performance-budget.json
```

Example budget:

```json
{
  "maxMeanRegressionPercent": 5,
  "maxAllocationRegressionPercent": 10
}
```

## Baseline history

`perfagent history` lists archived runs with current/anchor labels, followed by baseline
Created, Reset, and Promoted events, their timestamps, previous and selected RunIds,
and the active Current and Anchor. Events are displayed and replayed in append order,
including when timestamps are equal or the system clock moves backwards.

The append-only `baseline-events.jsonl` is authoritative. Readers ignore stale or
missing derived pointers when events exist for that baseline kind. Older pointer-only
baselines remain readable until their first event is recorded. Invalid event history
fails explicitly rather than silently falling back to a pointer.

`baseline set` and `baseline anchor` record Created or Reset events. History also
shows Promoted events recorded by other callers; it does not automatically promote runs.

## CI performance gate

A deterministic GitHub Actions example is included in `.github/workflows/performance-gate-demo.yml`. It demonstrates a version-controlled baseline, candidate evidence, and performance budget without depending on benchmark timing noise. See [GitHub performance regression gate](docs/github-performance-gate.md).

## Non-goals for V1

No hosted dashboard, accounts, database, IDE extension, distributed runners, or mandatory cloud service.

## License

Not selected yet. Do not assume redistribution terms until a license is added.
