# Trusted verdict scenario matrix

V0.3 includes a deterministic end-to-end scenario matrix for the `check` command.
It does not run BenchmarkDotNet; the scenarios use fixed evidence so CI validates
verdict behavior without benchmark timing noise.

The matrix covers:

1. clear PASS when the complete mean regression bounds stay within budget;
2. clear FAIL when the complete mean regression bounds exceed budget;
3. INCONCLUSIVE when measurement uncertainty crosses the mean budget boundary;
4. definite allocation FAIL winning over an inconclusive mean decision;
5. INCONCLUSIVE for logical processor-count mismatch;
6. INCONCLUSIVE for Server GC mismatch;
7. exact self-comparison producing a deterministic 0% PASS.

Each scenario asserts both the process exit code and the machine-readable JSON verdict.

Run locally after a Release build:

```bash
bash tests/e2e/trusted-verdict-scenarios.sh
```

This matrix is a product contract, not a statistical benchmark. Real BenchmarkDotNet
execution is covered separately by the CLI and installed-tool end-to-end smoke tests.
