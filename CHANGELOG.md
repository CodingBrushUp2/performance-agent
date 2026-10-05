# Changelog

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
