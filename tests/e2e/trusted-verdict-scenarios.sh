#!/usr/bin/env bash
set -euo pipefail

repo_root="$PWD"
perfagent=(dotnet "$repo_root/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll")
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

budget="$work/budget.json"
cat > "$budget" <<'JSON'
{"maxMeanRegressionPercent":5,"maxAllocationRegressionPercent":10}
JSON

write_evidence() {
  local path="$1"
  local mean="$2"
  local allocation="$3"
  local lower="$4"
  local upper="$5"
  local processors="$6"
  local server_gc="$7"

  cat > "$path" <<JSON
{
  "schemaVersion": "1.0",
  "environment": {
    "runtime": ".NET 10",
    "operatingSystem": "Linux",
    "architecture": "X64",
    "logicalProcessorCount": $processors,
    "serverGarbageCollection": $server_gc
  },
  "measurements": [
    {
      "name": "Scenario.Work",
      "meanNanoseconds": $mean,
      "allocatedBytesPerOperation": $allocation,
      "statistics": {
        "sampleCount": 15,
        "medianNanoseconds": $mean,
        "standardDeviationNanoseconds": 0.1,
        "standardErrorNanoseconds": 0.03,
        "outlierCount": 0,
        "confidenceIntervalLowerNanoseconds": $lower,
        "confidenceIntervalUpperNanoseconds": $upper,
        "confidenceLevelPercent": 99.9
      }
    }
  ]
}
JSON
}

run_scenario() {
  local name="$1"
  local expected_exit="$2"
  local expected_verdict="$3"
  local baseline="$4"
  local candidate="$5"

  set +e
  output="$("${perfagent[@]}" check --baseline "$baseline" --candidate "$candidate" --budget "$budget" --format json)"
  code=$?
  set -e

  if [ "$code" -ne "$expected_exit" ]; then
    echo "$name: expected exit $expected_exit, got $code" >&2
    printf '%s\n' "$output" >&2
    exit 1
  fi

  python3 -c '
import json, sys
name, expected = sys.argv[1], sys.argv[2]
document = json.load(sys.stdin)
actual = document["verdict"]
if actual != expected:
    raise SystemExit(f"${name}: expected verdict ${expected}, got ${actual}")
checks = document["checks"]
if len(checks) != 1:
    raise SystemExit(f"${name}: expected one check, got ${len(checks)}")
print(f"${name}: ${actual.upper()}")
' "$name" "$expected_verdict" <<< "$output"
}

baseline="$work/baseline.json"
candidate="$work/candidate.json"

write_evidence "$baseline" 100 1000 99.9 100.1 8 false
write_evidence "$candidate" 104 1080 103.9 104.1 8 false
run_scenario "clear-pass" 0 "pass" "$baseline" "$candidate"

write_evidence "$candidate" 106 1070 105.9 106.1 8 false
run_scenario "clear-mean-fail" 1 "fail" "$baseline" "$candidate"

write_evidence "$candidate" 106 1070 104 108 8 false
run_scenario "mean-boundary-inconclusive" 2 "inconclusive" "$baseline" "$candidate"

write_evidence "$candidate" 106 1300 104 108 8 false
run_scenario "allocation-fail-wins" 1 "fail" "$baseline" "$candidate"

write_evidence "$candidate" 104 1080 103.9 104.1 4 false
run_scenario "processor-mismatch" 2 "inconclusive" "$baseline" "$candidate"

write_evidence "$candidate" 104 1080 103.9 104.1 8 true
run_scenario "server-gc-mismatch" 2 "inconclusive" "$baseline" "$candidate"

run_scenario "exact-self-comparison" 0 "pass" "$baseline" "$baseline"

echo "Trusted verdict scenario matrix: PASS"
