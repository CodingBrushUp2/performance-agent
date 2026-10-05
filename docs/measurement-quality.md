# Measurement quality evidence

V0.3 is moving from a binary performance gate toward a trusted verdict.

This slice preserves statistical facts that BenchmarkDotNet already computes. It does
not yet change PASS, FAIL, or INCONCLUSIVE based on a new quality threshold.

Each normalized benchmark measurement may now include:

- sample count
- median duration
- standard deviation
- standard error
- outlier count

Evidence produced before this change remains valid. The `statistics` object is
optional so archived schema 1.0 evidence still round-trips and can still be compared.

## Why this is separate from quality policy

BenchmarkDotNet already performs substantial measurement and statistical analysis.
Performance Agent should not invent an arbitrary confidence threshold and silently
override BenchmarkDotNet semantics.

The next slice can define an explicit, tested quality policy using these preserved
facts. That policy should state exactly when insufficient or unstable evidence turns
a verdict into INCONCLUSIVE.

Until then, these statistics are evidence only; existing deterministic budget verdicts
are unchanged.
