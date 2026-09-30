# Compare sample

The current CLI accepts normalized evidence and produces a portable report.

From the repository root:

```bash
dotnet run --project apps/cli/PerformanceAgent.Cli -- compare MapOrder 100 80 1000 750 markdown
```

Expected evidence:

- mean time: 100 ns -> 80 ns (-20%)
- allocation: 1000 B/op -> 750 B/op (-25%)

JSON output:

```bash
dotnet run --project apps/cli/PerformanceAgent.Cli -- compare MapOrder 100 80 1000 750 json
```

This sample is intentionally deterministic. Benchmark execution is demonstrated separately by the BenchmarkDotNet integration test.
