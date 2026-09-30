#!/usr/bin/env bash
set -euo pipefail

output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- compare MapOrder 100 80 1000 750 markdown)"

grep -F "| MapOrder | Mean (ns) | 100 | 80 | -20% | Comparable |" <<< "$output"
grep -F "| MapOrder | Allocated (B/op) | 1000 | 750 | -25% | Comparable |" <<< "$output"


run_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- run samples/run/PerformanceAgent.SampleBenchmarks.csproj)"

grep -F '"schemaVersion":"1.0"' <<< "$run_output"
grep -F '"name":"Sum"' <<< "$run_output"
grep -F '"meanNanoseconds":' <<< "$run_output"
grep -F '"allocatedBytesPerOperation":' <<< "$run_output"
