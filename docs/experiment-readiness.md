# Experiment readiness

V0.6 starts by connecting changed-code candidate hints to the existing measurement
workflow without pretending that static analysis can prove benchmark coverage.

```bash
perfagent readiness \
  --base origin/main \
  --benchmark Benchmarks/Benchmarks.csproj

perfagent readiness \
  --base HEAD \
  --working-tree \
  --benchmark Benchmarks/Benchmarks.csproj \
  --target 'src/Checkout.cs::Checkout.Process(Order)' \
  --format json
```

The command checks four independent facts:

1. the selected Git diff contains one or more deterministic C# candidate hints;
2. one candidate target is selected (a single candidate is selected automatically);
3. the explicit benchmark project passes pre-run benchmark validation;
4. a trusted Current baseline exists.

The effective workspace budget and its source are also reported.

## Status

- `READY_WITH_UNVERIFIED_ASSUMPTIONS` / exit 0 means the mechanical prerequisites are
  present, but Performance Agent has **not** proven that the benchmark exercises the
  selected changed member or that the selected Current baseline represents the same
  benchmark scenario.
- `NEEDS_INPUT` / exit 1 means one or more prerequisites are missing.
- command, Git, build, configuration, archive, or validation execution failures use
  exit 2.

The JSON contract uses schema `1.0` and includes candidate targets, selected target,
benchmark validation summary, Current baseline RunId, effective budget, blockers, and
next actions.

## Deliberate boundary

Benchmark coverage and Current-baseline scenario compatibility remain `unverified`
in this slice. A developer or coding agent must confirm both before trusting a new
measurement comparison.

V0.6 does not generate benchmark code, infer production hot paths, invent realistic
inputs, or claim that a changed method is performance-sensitive.
