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
grep -F '"environment":' <<< "$run_output"
grep -F '"runtime":' <<< "$run_output"
grep -F '"operatingSystem":' <<< "$run_output"
grep -F '"architecture":' <<< "$run_output"

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
rm -rf "$temp_root"


# Current baseline resets must not hide cumulative regression against the anchor.
drift_root="$(mktemp -d)"
drift_candidate="$drift_root/candidate.json"
mkdir -p "$drift_root/.performance-agent/archive" "$drift_root/.performance-agent/baselines"
for spec in "anchor:100" "current:108"; do
  run_id="${spec%%:*}"
  mean="${spec##*:}"
  cat > "$drift_root/.performance-agent/archive/$run_id.json" <<JSON
{"runId":"$run_id","timestamp":"2026-09-30T12:00:00+00:00","commitSha":null,"evidence":{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":$mean,"allocatedBytesPerOperation":1000}]}}
JSON
done
cat > "$drift_root/.performance-agent/baselines/anchor.json" <<'JSON'
{"runId":"anchor"}
JSON
cat > "$drift_root/.performance-agent/baselines/current.json" <<'JSON'
{"runId":"current"}
JSON
cat > "$drift_candidate" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":112,"allocatedBytesPerOperation":1000}]}
JSON

set +e
drift_output="$(cd "$drift_root" && dotnet "$repo_root/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll" check --candidate "$drift_candidate" --budget "$repo_root/samples/ci/performance-budget.json")"
drift_code=$?
set -e
test "$drift_code" -eq 1
grep -F "Current baseline:" <<< "$drift_output"
grep -F "108 -> 112 (+3.7%)" <<< "$drift_output"
grep -F "Anchor baseline:" <<< "$drift_output"
grep -F "100 -> 112 (+12%)" <<< "$drift_output"
grep -F "Overall: FAIL" <<< "$drift_output"
rm -rf "$drift_root"


# Short aliases map to the same canonical check options.
alias_output="$(cd "$temp_root" && dotnet "$repo_root/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll" check -r run-temp -c "$temp_candidate" -p "$repo_root/samples/ci/performance-budget.json")"
grep -F "Overall: PASS" <<< "$alias_output"
