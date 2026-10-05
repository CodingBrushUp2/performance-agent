# Calibration JSON contract

Status: V0.3 contract, schema `1.0`

Use:

```bash
perfagent calibrate MyBenchmarks.csproj --runs 3 --max-spread 5 --format json
```

Progress and BenchmarkDotNet diagnostics remain on stderr. Stdout contains only the
machine-readable calibration result.

## Exit codes

- `0`: STABLE
- `1`: UNSTABLE
- `2`: invalid input or execution failure
- `130`: cancelled

Calibration never selects or changes Current/Anchor baselines.

## Fields

- `schemaVersion`: currently `1.0`
- `status`: `stable` or `unstable`
- `runCount`: number of independent benchmark runs
- `runIds`: immutable archived runs created by calibration
- `maxAllowedSpreadPercent`: configured repeatability threshold
- `metrics`: per-benchmark medians and cross-run spreads
- `reasons`: deterministic instability explanations

Each metric contains:

- benchmark identity
- median mean duration
- mean spread percentage
- median allocation when available
- allocation spread percentage when available

Unavailable allocation values stay JSON `null`.

## Relationship to trusted verdicts

Calibration is a repeatability preflight, not a hidden input to `perfagent check`.
It answers whether repeated runs in the current environment are stable enough under
the chosen spread policy.

The check verdict separately evaluates baseline vs candidate evidence using
performance budgets, environment comparability, and BenchmarkDotNet uncertainty.

This separation is intentional: Performance Agent does not silently select a baseline
or turn an arbitrary calibration threshold into an implicit PASS/FAIL policy.
