#!/usr/bin/env bash
set -euo pipefail

help_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- --help)"
grep -F "Performance Agent" <<< "$help_output"
grep -F "Commands:" <<< "$help_output"
grep -F "Typical workflow:" <<< "$help_output"

for command in run check analyze; do
  command_help="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- "$command" --help)"
  grep -F "Performance Agent - $command" <<< "$command_help"
  grep -F "Usage:" <<< "$command_help"
done

output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- compare MapOrder 100 80 1000 750 markdown)"

grep -F "| MapOrder | Mean (ns) | 100 | 80 | -20% | Comparable |" <<< "$output"
grep -F "| MapOrder | Allocated (B/op) | 1000 | 750 | -25% | Comparable |" <<< "$output"


run_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- run samples/run/PerformanceAgent.SampleBenchmarks.csproj)"
printf '%s\n' "$run_output"

grep -F '"schemaVersion": "1.0"' <<< "$run_output"
grep -F '"name": "SampleBenchmark.Sum"' <<< "$run_output"
grep -F '"meanNanoseconds":' <<< "$run_output"
grep -F '"allocatedBytesPerOperation":' <<< "$run_output"
grep -F '"environment":' <<< "$run_output"
grep -F '"runtime":' <<< "$run_output"
grep -F '"operatingSystem":' <<< "$run_output"
grep -F '"architecture":' <<< "$run_output"
grep -F '"statistics":' <<< "$run_output"
grep -F '"sampleCount":' <<< "$run_output"
grep -F '"medianNanoseconds":' <<< "$run_output"
grep -F '"standardDeviationNanoseconds":' <<< "$run_output"
grep -F '"standardErrorNanoseconds":' <<< "$run_output"
grep -F '"outlierCount":' <<< "$run_output"

if grep -Fq '// BenchmarkDotNet' <<< "$run_output"; then
  echo "BenchmarkDotNet diagnostic output leaked into normalized evidence" >&2
  exit 1
fi


baseline_file="$(mktemp)"
candidate_file="$(mktemp)"
trap 'rm -f "$baseline_file" "$candidate_file"' EXIT

cat > "$baseline_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":100,"allocatedBytesPerOperation":1000}]}
JSON
cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080}]}
JSON

check_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10)"
grep -F "MapOrder: PASS" <<< "$check_output"
grep -F "Overall: PASS" <<< "$check_output"

check_json="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10 --format json)"
grep -F '"schemaVersion": "1.0"' <<< "$check_json"
grep -F '"verdict": "pass"' <<< "$check_json"
grep -F '"candidate":' <<< "$check_json"
grep -F '"kind": "explicit"' <<< "$check_json"
grep -F '"name": "MapOrder"' <<< "$check_json"
grep -F '"status": "comparable"' <<< "$check_json"
grep -F '"budgetExceeded": false' <<< "$check_json"

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":106,"allocatedBytesPerOperation":1070}]}
JSON

set +e
failure_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10)"
failure_code=$?
set -e
test "$failure_code" -eq 1
grep -F "MapOrder: FAIL" <<< "$failure_output"
grep -F "Overall: FAIL" <<< "$failure_output"

set +e
failure_json="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10 --format json)"
failure_json_code=$?
set -e
test "$failure_json_code" -eq 1
grep -F '"verdict": "fail"' <<< "$failure_json"
grep -F '"budgetExceeded": true' <<< "$failure_json"

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":null}]}
JSON

set +e
inconclusive_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10)"
inconclusive_code=$?
set -e
test "$inconclusive_code" -eq 2
grep -F "MapOrder: INCONCLUSIVE" <<< "$inconclusive_output"
grep -F "required allocation metric is not comparable (Unavailable)" <<< "$inconclusive_output"
grep -F "Overall: INCONCLUSIVE" <<< "$inconclusive_output"


budget_file="$(mktemp)"
cat > "$budget_file" <<'JSON'
{"maxMeanRegressionPercent":5,"maxAllocationRegressionPercent":10}
JSON
trap 'rm -f "$baseline_file" "$candidate_file" "$budget_file"' EXIT

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080}]}
JSON

budget_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" --budget "$budget_file")"
grep -F "MapOrder: PASS" <<< "$budget_output"
grep -F "Overall: PASS" <<< "$budget_output"


evidence_file="$(mktemp)"
dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- run samples/run/PerformanceAgent.SampleBenchmarks.csproj --output "$evidence_file"
test -s "$evidence_file"
grep -F '"schemaVersion": "1.0"' "$evidence_file"
grep -F '"environment":' "$evidence_file"
rm -f "$evidence_file"


# Temporary archived baseline must be command-local and must not mutate baseline state/history.
temp_root="$(mktemp -d)"
temp_candidate="$temp_root/candidate.json"
mkdir -p "$temp_root/.performance-agent/archive"
cat > "$temp_root/.performance-agent/archive/run-temp.json" <<'JSON'
{"runId":"run-temp","timestamp":"2026-09-30T12:00:00+00:00","commitSha":"abc123","evidence":{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":100,"allocatedBytesPerOperation":1000}]}}
JSON
cat > "$temp_candidate" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080}]}
JSON

repo_root="$PWD"
temporary_output="$(cd "$temp_root" && dotnet "$repo_root/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll" check --rid run-temp --candidate "$temp_candidate" --budget "$repo_root/samples/ci/performance-budget.json")"
grep -F "MapOrder: PASS" <<< "$temporary_output"
grep -F "Overall: PASS" <<< "$temporary_output"

# Keyed options are intentionally order-independent.
temporary_reordered_output="$(cd "$temp_root" && dotnet "$repo_root/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll" check --budget "$repo_root/samples/ci/performance-budget.json" --candidate "$temp_candidate" --rid run-temp)"
grep -F "Overall: PASS" <<< "$temporary_reordered_output"

test ! -e "$temp_root/.performance-agent/baselines/current.json"
test ! -e "$temp_root/.performance-agent/baselines/anchor.json"
test ! -e "$temp_root/.performance-agent/baseline-events.jsonl"