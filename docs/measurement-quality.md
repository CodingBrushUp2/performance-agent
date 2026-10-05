# Measurement quality policy

V0.3 uses measurement uncertainty to decide whether a mean-performance budget can be
trusted.

Performance Agent preserves BenchmarkDotNet's 99.9% confidence interval for every
new benchmark measurement. The verdict policy compares the whole possible regression
range against the configured mean budget.

For a baseline confidence interval `[B_low, B_high]` and candidate interval
`[C_low, C_high]`:

```text
minimum regression = (C_low / B_high - 1) * 100
maximum regression = (C_high / B_low - 1) * 100
```

The decision is:

- `PASS` for mean when the maximum possible regression is at or below the budget.
- `FAIL` for mean when the minimum possible regression is above the budget.
- `INCONCLUSIVE` when the confidence range crosses the budget boundary.

This deliberately avoids a separate arbitrary rule such as "standard deviation must
be below 5%". The relevant question is whether the uncertainty can change the
PASS/FAIL decision.

## Required evidence

A trusted mean verdict requires:

- benchmark statistics for baseline and candidate
- both confidence interval bounds
- confidence-level provenance
- BenchmarkDotNet's 99.9% confidence level
- a positive baseline confidence interval suitable for ratio comparison

Evidence created before statistical metadata was preserved is still readable, but a
configured mean budget becomes `INCONCLUSIVE` until baseline and candidate are
re-run with the current Performance Agent.

If only an allocation budget is configured, missing timing statistics do not block
that allocation-only decision.

## Definite failures still win

If another configured metric produces a definite failure, for example allocation is
over budget, the overall benchmark verdict remains `FAIL` even when the mean timing
decision is inconclusive.

## JSON contract

The machine-readable check output includes a `meanDecision` object with:

- decision status
- minimum possible regression
- maximum possible regression
- confidence level
- reason when inconclusive

The existing `mean.budgetExceeded` field remains the point-estimate budget check.
It is not the authoritative mean verdict when uncertainty is present. Consumers
should use the benchmark verdict and `meanDecision.status`.
