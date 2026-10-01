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

The package includes BenchmarkHost and its runtime dependencies. A stable .NET 10 SDK
is required to build and execute your BenchmarkDotNet project; the Performance Agent
source tree is not required after installation. Benchmark projects are executable code,
so run only projects you trust.

Then run:

```bash
perfagent run path/to/Benchmarks.csproj
perfagent calibrate path/to/Benchmarks.csproj
perfagent check baseline.json candidate.json --budget performance-budget.json
```

Calibration runs the benchmark repeatedly (3 runs by default) and reports timing/allocation medians and cross-run spread. A stable calibration never changes `Current` or `Anchor`; baseline selection remains an explicit user action. Use `--runs <count>` and `--max-spread <percent>` to tune the calibration check.

Example budget:

```json
{
  "maxMeanRegressionPercent": 5,
  "maxAllocationRegressionPercent": 10
}
```

To verify the installed execution path locally after building the solution:

```bash
bash tests/e2e/installed-tool-smoke.sh
```

This packs a local package, installs it with an isolated tool manifest, and measures a
standalone BenchmarkDotNet project outside the repository. Only the smoke benchmark
uses a dry job; production runs retain the benchmark's normal BenchmarkDotNet configuration.

Press Ctrl+C during `perfagent run` to cancel project inspection, build, or benchmark
execution. The CLI terminates the active child process tree, removes temporary host
evidence, reports cancellation, and exits with code 130. Measurements completed and
archived before cancellation remain valid; an interrupted benchmark is not archived.

## Try the first usable workflow

A small benchmark project is included so the current product can be exercised immediately. On macOS/Linux, the shortest path is:\n\n```bash\nbash scripts/try-local.sh\n```\n\nThe equivalent manual steps are:

```bash
dotnet build PerformanceAgent.sln -c Release
dotnet pack apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj -c Release --no-build -o artifacts
dotnet tool install --tool-path ./.tools PerformanceAgent.Cli --version 0.1.0 --add-source ./artifacts

./.tools/perfagent calibrate samples/QuickStartBenchmarks/QuickStartBenchmarks.csproj
./.tools/perfagent history
```

Calibration archives each measured run but intentionally leaves Current and Anchor unset. Inspect the reported RunIds, then explicitly select the run you trust:

```bash
./.tools/perfagent baseline set <run-id>
./.tools/perfagent baseline anchor <run-id>
./.tools/perfagent history
```

This is the first operational local workflow: measure a real BenchmarkDotNet project, evaluate cross-run stability, retain immutable evidence, and explicitly establish baseline provenance. No AI, cloud account, database, or monitoring platform is required.

After selecting a trusted RunId, re-run the same benchmark and enforce the sample performance budget with:

```bash
bash scripts/check-local.sh <run-id>
```

The command exits 0 when the candidate stays within budget, 1 on a measured regression, and 2 when the evidence cannot be compared or the command is invalid. The candidate run is still archived, while the selected baseline remains unchanged.


## Archive identities

Generated RunIds use an invariant UTC timestamp and random suffix. Safe custom IDs
remain supported: append, read, and history listing use the same identity. IDs must
be portable filenames, without path separators, reserved filename characters or
device names, or trailing dots/spaces. An archive file's RunId must match its filename.
All archive JSON files are listed; temporary files are ignored. Duplicate IDs never
overwrite existing runs, including concurrent appends.

## Baseline history

`perfagent history` lists archived runs with current/anchor labels, followed by baseline
Created, Reset, and Promoted events, their timestamps, previous and selected RunIds,
and the active Current and Anchor. Events are displayed and replayed in append order,
including when timestamps are equal or the system clock moves backwards.

The append-only `baseline-events.jsonl` is authoritative. Readers ignore stale or
missing derived pointers when events exist for that baseline kind. Older pointer-only
baselines remain readable until their first event is recorded. Invalid event history
fails explicitly rather than silently falling back to a pointer.

Event appends validate unique event IDs, transition chains, and both target/previous
archive references before writing. Cooperating writers use an exclusive file handle;
a busy history fails explicitly and can be retried. Cancelled or failed writes roll back
only their new bytes. Corrupt history is reported with a line number and is never
silently repaired. Baseline reads validate all referenced runs, including superseded
selections. A first Reset/Promoted event may retain a legacy baseline's previous RunId.

`baseline set` and `baseline anchor` record Created or Reset events. History also
shows Promoted events recorded by other callers; it does not automatically promote runs.

## CI performance gate

A deterministic GitHub Actions example is included in `.github/workflows/performance-gate-demo.yml`. It demonstrates a version-controlled baseline, candidate evidence, and performance budget without depending on benchmark timing noise. See [GitHub performance regression gate](docs/github-performance-gate.md).

## Non-goals for V1

No hosted dashboard, accounts, database, IDE extension, distributed runners, or mandatory cloud service.

## License

Not selected yet. Do not assume redistribution terms until a license is added.
