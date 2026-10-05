# Changelog

## 0.5.0

V0.5 adds conservative changed-code candidate discovery for developers and coding agents.

### Added

- `perfagent candidates --base <git-ref>` for deterministic C# diff-to-member mapping;
- Roslyn-based mapping of changed lines to touched methods and members;
- configurable candidate limits with text and schema 1.0 JSON output;
- support for committed, staged, and unstaged working-tree changes via `--working-tree`;
- installed-tool and CLI end-to-end coverage for candidate discovery and Roslyn packaging.

### Behavior

- candidate ranking is deterministic and based on changed-line overlap;
- test, generated, bin, and obj paths are filtered out;
- option-like Git refs are rejected before invoking Git;
- candidate hints are focus hints only, not performance-risk verdicts;
- `--working-tree` and explicit `--head` are mutually exclusive.

### Design boundaries

- no AI ranking or benchmark generation;
- no inferred hot-path or complexity claims;
- no assembly browser or full-member enumeration;
- runtime profiles and stronger ranking signals are deferred until there is real evidence
  that they improve precision.

## 0.4.0

V0.4 focuses on benchmark validity before measurement.

### Added

- `perfagent validate <benchmark.csproj>` for pre-run BenchmarkDotNet validation;
- text and versioned JSON validation output;
- validation-only discovery that can surface invalid non-public benchmark declarations
  without changing normal benchmark discovery;
- PA1001 warning for trivial benchmark bodies;
- PA1002 warning for direct manual timing with `Stopwatch` inside the measured workload;
- PA1003 warning for direct `GC.Collect()` calls inside the measured workload;
- installed-tool and CLI end-to-end coverage for validation behavior.

### Behavior

- `VALID` returns exit code 0;
- structurally invalid benchmark declarations return exit code 1;
- build, input, load, discovery, timeout, or validation-execution failures return exit code 2;
- PA1001/PA1002/PA1003 remain non-blocking warnings and do not turn a structurally valid benchmark into INVALID.

### Design boundaries

- BenchmarkDotNet remains authoritative for declaration/configuration validation;
- Performance Agent-specific rules only cover narrow quality gaps not already handled by BenchmarkDotNet;
- no benchmark generation, target-discovery browser, Java/JMH, AI Monitoring integration,
  hosted service, or new AI behavior was added in V0.4.

## 0.3.0

V0.3 focuses on making Performance Agent a trustworthy deterministic performance judge
for developers, CI pipelines, and coding agents.

### Added

- first-class `PASS`, `FAIL`, and `INCONCLUSIVE` performance verdicts;
- versioned machine-readable JSON output for `perfagent check`;
- machine-readable `STABLE` / `UNSTABLE` calibration JSON output;
- preserved BenchmarkDotNet statistics including sample count, median, standard
  deviation, standard error, outliers, and confidence interval provenance;
- confidence-aware mean-budget decisions using BenchmarkDotNet 99.9% intervals;
- extended environment fingerprint with logical processor count and Server GC mode;
- deterministic trusted-verdict scenario matrix covering success, regression,
  uncertainty, allocation failure, and environment mismatch.

### Changed

- unavailable required metrics, environment mismatches, and statistically ambiguous
  mean results no longer silently pass;
- legacy evidence remains readable, but insufficient statistical/environment metadata
  may produce `INCONCLUSIVE` rather than a false confident verdict;
- CLI exit codes consistently map deterministic verdicts to 0/1/2 for
  PASS/FAIL/INCONCLUSIVE.

### Unchanged

- AI remains optional and advisory;
- BenchmarkDotNet measurements remain authoritative;
- baseline selection remains explicit;
- no AI Monitoring integration, target-discovery UI, benchmark generation, Java/JMH,
  or hosted service was added in V0.3.

## 0.2.0

Introduced optional AI analysis over deterministic benchmark evidence, local Web UI
analysis, and release/UX hardening while keeping AI advisory.
