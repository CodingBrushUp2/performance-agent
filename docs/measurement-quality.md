# Measurement quality policy

V0.3 uses measurement uncertainty to decide whether a mean-performance budget can be
trusted.

Performance Agent preserves BenchmarkDotNet's 99.9% confidence interval for every
new benchmark measurement. The verdict policy derives conservative regression bounds
from the baseline and candidate intervals and compares those bounds with the configured
mean budget. These derived bounds are not themselves a 99.9% confidence interval for
the ratio; 99.9% is the confidence level of each source BenchmarkDotNet interval.

For a baseline confidence interval `[B_low, B_high]` and candidate interval
`[C_low, C_high]`:

```text
minimum regression = (C_low / B_high - 1) * 100
maximum regression = (C_high / B_low - 1) * 100
```

The decision is:

- `PASS` for mean when the maximum possible regression is at or below the budget.
- `FAIL` for mean when the minimum possible regression is above the budget.
- `INCONCLUSIVE` when the derived regression bounds cross the budget boundary.

This deliberately avoids a separate arbitrary rule such as "standard deviation must
be below 5%". The relevant question is whether the uncertainty can change the
PASS/FAIL decision.

## Exact self-comparison

When baseline and candidate are the exact same normalized measurement, the measured
change is deterministically 0%. Performance Agent returns a conclusive within-budget
mean decision without treating the same confidence interval as two independent runs.

This matters for workflows such as viewing or analyzing a run that is itself the
active Current baseline.

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
- source confidence level used by the BenchmarkDotNet intervals
- reason when inconclusive

The existing `mean.budgetExceeded` field remains the point-estimate budget check.
It is not the authoritative mean verdict when uncertainty is present. Consumers
should use the benchmark verdict and `meanDecision.status`.


## Environment comparability

New benchmark evidence also records logical processor count and whether Server GC was
enabled. Performance Agent rejects comparisons when these facts differ.

For backward compatibility:

- two legacy evidence documents that both lack the extended fields remain comparable
  under the older runtime/OS/architecture rule;
- comparing legacy evidence with new evidence is INCONCLUSIVE because the environment
  fingerprint is present on only one side;
- two new evidence documents must agree on runtime, operating system, architecture,
  logical processor count, and Server GC mode.

CPU model is intentionally not part of this slice. Cross-platform CPU identification
needs a separate design rather than brittle OS-specific parsing.
