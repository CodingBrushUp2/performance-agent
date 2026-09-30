# CI sample

The checked-in JSON fixtures keep the repository's regression-gate demo deterministic.

For a controlled benchmark runner, generate evidence directly to a file:

```bash
perfagent run path/to/Benchmarks.csproj --output candidate.json
perfagent check baseline.json candidate.json --budget performance-budget.json
```

After a reviewed performance change is accepted, the candidate evidence can become the next baseline according to the team's baseline policy.

Do not automatically promote every passing candidate to baseline. Baseline promotion should be an explicit reviewed action, otherwise gradual regressions can be normalized over time.
