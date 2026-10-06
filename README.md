# Performance Agent

**Did this change actually make my .NET code slower?** Performance Agent answers that
question with a deterministic **PASS / FAIL / INCONCLUSIVE** verdict, built on
BenchmarkDotNet measurements and explicit performance budgets.

> **AI proposes. Measurements decide.** The core workflow needs no LLM. Optional AI
> analysis explains measured evidence, but it can never change a verdict or an exit code.

Built for developers, CI pipelines, and coding agents: every command has stable JSON
output and meaningful exit codes (`0` pass, `1` regression, `2` inconclusive or invalid).

## Why it exists

BenchmarkDotNet measures very well, but it leaves the decision to you: is a 4% change
a regression or noise? Is the baseline you're comparing against still trustworthy?
Performance Agent turns measurements into a decision you can gate on, and it says
**INCONCLUSIVE** instead of guessing when the evidence is too noisy or the environments
don't match.

## What it does

- **Trusted verdicts:** a mean regression is only PASS or FAIL when the full 99.9%
  confidence-derived range lies on one side of the budget; otherwise it is INCONCLUSIVE.
- **Environment checks:** evidence from mismatched runtimes, OSes, architectures,
  processor counts, or GC modes yields INCONCLUSIVE instead of a misleading verdict.
- **Calibration:** repeated runs quantify cross-run spread before you trust a baseline.
- **Evidence history:** immutable archived runs with provenance, plus explicitly chosen
  Current and Anchor baselines. The anchor preserves the original reference, so
  resetting Current cannot quietly normalize gradual regressions.
- **Benchmark validity warnings:** trivial benchmark bodies, manual `Stopwatch` timing,
  and forced GC inside the measured region.
- **Changed-code hints:** Roslyn maps a Git diff to the C# members it touched, so you
  know which benchmarks matter for a change.
- **Reports:** JSON, Markdown, standalone HTML, and a local (loopback-only) web UI.
- **Optional AI analysis:** grounded in archived evidence and validated structured
  output; advisory only.

## Quick start

Requires the .NET 10 SDK.

```bash
dotnet build PerformanceAgent.sln -c Release
dotnet pack apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj -c Release --no-build -o artifacts
dotnet tool install --tool-path ./.tools PerformanceAgent.Cli --version 0.5.0 --add-source ./artifacts

./.tools/perfagent run path/to/Benchmarks.csproj --output baseline.json
# ...change the code...
./.tools/perfagent run path/to/Benchmarks.csproj --output candidate.json
./.tools/perfagent check baseline.json candidate.json --budget performance-budget.json
```

```json
{ "maxMeanRegressionPercent": 5, "maxAllocationRegressionPercent": 10 }
```

Add `--format json` to `check` for the versioned
[verdict contract](docs/check-verdict-json.md) used by CI and agents.

## Case study

[performance-agent-realworld-test](https://github.com/CodingBrushUp2/performance-agent-realworld-test)
is an independent BenchmarkDotNet project. Its workflow builds this tool from source and,
on one GitHub-hosted runner, measures a `StringBuilder` baseline against a deliberate
`string +=` regression, plus a no-change control
([latest run](https://github.com/CodingBrushUp2/performance-agent-realworld-test/actions/runs/37526050859); times in ns, allocations in bytes per operation):

```text
regression check (exit 1)
StringProcessingBenchmarks.BuildReport: FAIL
  Mean: 3558.11 -> 289144.7 (+8026.35%) (budget +5%) FAIL
  Allocation: 59200 -> 13038000 (+21923.65%) (budget +10%) FAIL
Overall: FAIL

no-change control (exit 0)
StringProcessingBenchmarks.BuildReport: PASS
  Mean: 3558.11 -> 3534.2 (-0.67%) (budget +5%) PASS
  Allocation: 59200 -> 59200 (0%) (budget +10%) PASS
Overall: PASS
```

Building this case study also exposed a real bug. The benchmark host inherited the
caller's working directory, and BenchmarkDotNet locates projects by name, so with
another same-named project nearby, `perfagent run` could **silently measure the wrong
code**. The first case-study run reported PASS for the regression because both sides had
measured the slow version. Fixed in [#119](https://github.com/CodingBrushUp2/performance-agent/pull/119);
the workflow's directory layout now guards against it. Absolute timings vary
between runners (another run measured a 7987 ns baseline); the verdicts did not.

## Design

```
src/PerformanceAgent.Core            comparison, budgets, verdicts, history (no package dependencies)
src/PerformanceAgent.BenchmarkDotNet  measurement adapter
src/PerformanceAgent.AI               optional, advisory analysis provider
apps/benchmark-host                   isolated child process that runs user benchmarks
apps/cli                              CLI and local web UI over the same services
```

Key decisions are documented in [measurement quality](docs/measurement-quality.md),
the [trusted-verdict scenarios](docs/trusted-verdict-scenarios.md),
[benchmark validity](docs/benchmark-validity.md), and [architecture](docs/architecture.md).
335 unit, integration, and end-to-end tests cover the verdict logic and CLI contracts.

## Limitations

These are deliberate boundaries, stated so a PASS is never read as more than it is:

- You write the benchmarks. The tool does not generate them or find production hot paths.
- Changed-member hints do not prove that a benchmark actually executes the changed code;
  `perfagent readiness` reports this as `READY_WITH_UNVERIFIED_ASSUMPTIONS`.
- Archived baselines can go stale. Environment checks and calibration reduce, but do
  not eliminate, machine-load drift; running baseline and candidate in the same job (as
  in the case study) is the most reliable setup.
- Benchmark projects are trusted executable code. Process isolation is not a sandbox.

## How this was built

Performance Agent was built in a short, intensive period with AI coding agents. The
product scope, architecture, and verdict policy were mine, and I reviewed the work;
agents wrote much of the implementation under that direction, as the Git history
shows openly. The project applies its own principle to its development: AI proposes,
and the tests and measurements decide.

## Status

Feature-complete for its intended scope (v0.5.0, plus early readiness checks in v0.6).
Maintained for bug and security fixes. No NuGet package is published yet.

**More:** [usage guide](docs/usage.md) · [changelog](CHANGELOG.md) ·
[security model](SECURITY.md) · [contributing](CONTRIBUTING.md)

## License

[MIT](LICENSE). Bundled dependencies keep their own licenses; see
[third-party notices](THIRD_PARTY_NOTICES.md).
