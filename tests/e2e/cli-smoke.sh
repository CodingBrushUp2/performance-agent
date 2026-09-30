#!/usr/bin/env bash
set -euo pipefail

output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- compare MapOrder 100 80 1000 750 markdown)"

grep -F "| MapOrder | Mean (ns) | 100 | 80 | -20% | Comparable |" <<< "$output"
grep -F "| MapOrder | Allocated (B/op) | 1000 | 750 | -25% | Comparable |" <<< "$output"


run_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- run samples/run/PerformanceAgent.SampleBenchmarks.csproj)"
printf '%s\n' "$run_output"

grep -F '"schemaVersion": "1.0"' <<< "$run_output"
grep -F '"name": "SampleBenchmark.Sum"' <<< "$run_output"
grep -F '"meanNanoseconds":' <<< "$run_output"
grep -F '"allocatedBytesPerOperation":' <<< "$run_output"

if grep -Fq '// BenchmarkDotNet' <<< "$run_output"; then
  echo "BenchmarkDotNet diagnostic output leaked into normalized evidence" >&2
  exit 1
fi


baseline_file="$(mktemp)"
candidate_file="$(mktemp)"
trap 'rm -f "$baseline_file" "$candidate_file"' EXIT

cat > "$baseline_file" <<'JSON'
{"schemaVersion":"1.0","measurements":[{"name":"MapOrder","meanNanoseconds":100,"allocatedBytesPerOperation":1000}]}
JSON
cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080}]}
JSON

check_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10)"
grep -F "MapOrder: PASS" <<< "$check_output"
grep -F "Overall: PASS" <<< "$check_output"

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","measurements":[{"name":"MapOrder","meanNanoseconds":106,"allocatedBytesPerOperation":1070}]}
JSON

set +e
failure_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10)"
failure_code=$?
set -e
test "$failure_code" -eq 1
grep -F "MapOrder: FAIL" <<< "$failure_output"
grep -F "Overall: FAIL" <<< "$failure_output"


budget_file="$(mktemp)"
cat > "$budget_file" <<'JSON'
{"maxMeanRegressionPercent":5,"maxAllocationRegressionPercent":10}
JSON
trap 'rm -f "$baseline_file" "$candidate_file" "$budget_file"' EXIT

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080}]}
JSON

budget_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" --budget "$budget_file")"
grep -F "MapOrder: PASS" <<< "$budget_output"
grep -F "Overall: PASS" <<< "$budget_output"
