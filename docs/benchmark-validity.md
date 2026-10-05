# Benchmark validity guard

V0.4 starts with a pre-run validation command:

```bash
perfagent validate MyBenchmarks.csproj
perfagent validate MyBenchmarks.csproj --format json
```

This command builds the benchmark project and runs BenchmarkDotNet's own declaration
and configuration validators without executing benchmark measurements.

Examples of issues BenchmarkDotNet can report include invalid benchmark signatures,
non-public benchmark methods, invalid setup/cleanup declarations, parameter-source
problems, generic benchmark declarations, and configuration/optimization issues.

Exit codes:

- `0`: VALID
- `1`: INVALID
- `2`: input, build, assembly-load, discovery, timeout, or validation execution error

The JSON result is schema `1.0` and contains the benchmark type count plus normalized
diagnostics with source, severity, benchmark type/method, and message.

## Why this comes first

Performance Agent should not duplicate checks BenchmarkDotNet already knows how to
perform. V0.4 first exposes those validators consistently through the same CLI/tool
workflow.

Performance Agent-specific validity rules can be added later only for gaps that
BenchmarkDotNet does not already cover.


## Performance Agent quality warnings

After BenchmarkDotNet's validators pass, Performance Agent can add non-blocking quality
warnings for suspicious benchmark shapes that are structurally valid but may not
measure meaningful work.

### PA1001: trivial benchmark body

PA1001 warns when a benchmark method compiles down to an empty body or a direct
constant/string/null return.

Example:

```csharp
[Benchmark]
public int ConstantWork() => 42;
```

This remains `VALID`; the warning asks the author to verify that the benchmark
actually exercises the intended code path and has not collapsed into a meaningless
measurement.

PA1001 is intentionally conservative and narrow. It does not attempt to prove general
dead-code elimination or constant folding, and it is not an error.


### PA1002: manual timing inside benchmark

PA1002 warns when a benchmark method directly calls timing APIs on
`System.Diagnostics.Stopwatch`, such as `Start`, `Restart`, `StartNew`, or
`GetTimestamp`.

Example:

```csharp
[Benchmark]
public long Work()
{
    var start = Stopwatch.GetTimestamp();
    DoWork();
    return Stopwatch.GetTimestamp() - start;
}
```

BenchmarkDotNet already owns the measurement clock. Nested/manual timing usually makes
the benchmark measure a different thing than intended and complicates interpretation.

PA1002 remains a warning because benchmarking Stopwatch itself can be intentional. The
rule is deliberately limited to direct Stopwatch calls in the benchmark body; it does
not recursively inspect helper methods.


### PA1003: forced garbage collection inside benchmark

PA1003 warns when a benchmark method directly calls `GC.Collect()` inside the measured
region. Forced collection can distort timing and allocation evidence and should usually
be moved into setup unless garbage collection itself is the intended subject.

The rule remains warning-only because benchmarking GC behavior can be intentional. It is
deliberately limited to direct calls from the benchmark workload; calls from
`GlobalSetup` are not flagged.
