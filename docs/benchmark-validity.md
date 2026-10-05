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


## Performance Agent warnings

Performance Agent may add non-blocking warnings for suspicious benchmark shapes that
BenchmarkDotNet accepts structurally.

The first custom rule warns when a benchmark body is an obvious no-op or constant
return at the IL level. Examples include an empty `void` benchmark or `return 42`.

These diagnostics are deliberately warnings, not errors:

- validation remains `VALID`;
- the command still exits with code `0`;
- ambiguous benchmark bodies are not flagged;
- the rule is intended to catch obviously meaningless measurements with low false
  positive risk.

This rule does not attempt full source-code analysis or claim that a benchmark is
semantically representative of production behavior.
