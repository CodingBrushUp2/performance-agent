#!/usr/bin/env bash
set -euo pipefail

help_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- --help)"
grep -F "Performance Agent" <<< "$help_output"
grep -F "Commands:" <<< "$help_output"
grep -F "Typical workflow:" <<< "$help_output"

for command in readiness candidates validate run check analyze; do
  command_help="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- "$command" --help)"
  grep -F "Performance Agent - $command" <<< "$command_help"
  grep -F "Usage:" <<< "$command_help"
done

readiness_repo="$(mktemp -d)"
cli_project="$PWD/apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj"
benchmark_project="$PWD/samples/run/PerformanceAgent.SampleBenchmarks.csproj"
(
  cd "$readiness_repo"
  git init -q
  git config user.email "perfagent@example.invalid"
  git config user.name "Performance Agent Tests"
  cat > Sample.cs <<'CS'
public class Sample
{
    public int Work()
    {
        return 1;
    }
}
CS
  git add Sample.cs
  git commit -q -m baseline
  readiness_base="$(git rev-parse HEAD)"
  cat > Sample.cs <<'CS'
public class Sample
{
    public int Work()
    {
        return 2;
    }
}
CS
  git add Sample.cs
  git commit -q -m candidate

  set +e
  readiness_output="$(dotnet run --project "$cli_project" --configuration Release --no-build -- readiness --base "$readiness_base" --benchmark "$benchmark_project")"
  readiness_code=$?
  set -e
  test "$readiness_code" -eq 1
  grep -F "Experiment readiness: NEEDS_INPUT" <<< "$readiness_output"
  grep -F "Target: Sample.cs::Sample.Work()" <<< "$readiness_output"
  grep -F "Benchmark: VALID" <<< "$readiness_output"
  grep -F "Current baseline: Not selected" <<< "$readiness_output"
  grep -F "Coverage: unverified" <<< "$readiness_output"
  grep -F "Baseline compatibility: not-assessed" <<< "$readiness_output"
  grep -F "No Current baseline is selected." <<< "$readiness_output"

  set +e
  readiness_json="$(dotnet run --project "$cli_project" --configuration Release --no-build -- readiness --base "$readiness_base" --benchmark "$benchmark_project" --format json)"
  readiness_json_code=$?
  set -e
  test "$readiness_json_code" -eq 1
  grep -F '"schemaVersion": "1.0"' <<< "$readiness_json"
  grep -F '"status": "needsInput"' <<< "$readiness_json"
  grep -F '"key": "Sample.cs::Sample.Work()"' <<< "$readiness_json"
  grep -F '"valid": true' <<< "$readiness_json"
  grep -F '"currentBaselineRunId": null' <<< "$readiness_json"
  grep -F '"coverageStatus": "unverified"' <<< "$readiness_json"
  grep -F '"baselineCompatibilityStatus": "not-assessed"' <<< "$readiness_json"
)
rm -rf "$readiness_repo"

validate_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate samples/run/PerformanceAgent.SampleBenchmarks.csproj)"
grep -F "Validation: VALID" <<< "$validate_output"
grep -F "Benchmark types: 1" <<< "$validate_output"

validate_json="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate samples/run/PerformanceAgent.SampleBenchmarks.csproj --format json)"
grep -F '"schemaVersion": "1.0"' <<< "$validate_json"
grep -F '"valid": true' <<< "$validate_json"
grep -F '"benchmarkTypeCount": 1' <<< "$validate_json"

warning_validate_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate tests/fixtures/SuspiciousBenchmarks/SuspiciousBenchmarks.csproj)"
grep -F "Validation: VALID" <<< "$warning_validate_output"
grep -F "[WARNING] PerformanceAgent.PA1001" <<< "$warning_validate_output"
grep -F "ConstantWork" <<< "$warning_validate_output"
grep -F "[WARNING] PerformanceAgent.PA1002" <<< "$warning_validate_output"
grep -F "ManualTiming" <<< "$warning_validate_output"

warning_validate_json="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate tests/fixtures/SuspiciousBenchmarks/SuspiciousBenchmarks.csproj --format json)"
grep -F '"valid": true' <<< "$warning_validate_json"
grep -F '"source": "PerformanceAgent.PA1001"' <<< "$warning_validate_json"
grep -F '"source": "PerformanceAgent.PA1002"' <<< "$warning_validate_json"
grep -F '"severity": "warning"' <<< "$warning_validate_json"

forced_gc_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate tests/fixtures/ForcedGcBenchmarks/ForcedGcBenchmarks.csproj)"
grep -F "Validation: VALID" <<< "$forced_gc_output"
grep -F "[WARNING] PerformanceAgent.PA1003" <<< "$forced_gc_output"
grep -F "GC.Collect()" <<< "$forced_gc_output"

forced_gc_json="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate tests/fixtures/ForcedGcBenchmarks/ForcedGcBenchmarks.csproj --format json)"
grep -F '"valid": true' <<< "$forced_gc_json"
grep -F '"source": "PerformanceAgent.PA1003"' <<< "$forced_gc_json"
grep -F '"severity": "warning"' <<< "$forced_gc_json"

set +e
invalid_validate_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate tests/fixtures/InvalidBenchmarks/InvalidBenchmarks.csproj 2>&1)"
invalid_validate_code=$?
set -e
test "$invalid_validate_code" -eq 1
grep -F "Validation: INVALID" <<< "$invalid_validate_output"
grep -F "Method must be public" <<< "$invalid_validate_output"

set +e
broken_validate_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- validate tests/fixtures/BrokenBenchmarks/BrokenBenchmarks.csproj 2>&1)"
broken_validate_code=$?
set -e
test "$broken_validate_code" -eq 2
grep -F "MissingType" <<< "$broken_validate_output"

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
grep -F '"logicalProcessorCount":' <<< "$run_output"
grep -F '"serverGarbageCollection":' <<< "$run_output"
grep -F '"statistics":' <<< "$run_output"
grep -F '"sampleCount":' <<< "$run_output"
grep -F '"medianNanoseconds":' <<< "$run_output"
grep -F '"standardDeviationNanoseconds":' <<< "$run_output"
grep -F '"standardErrorNanoseconds":' <<< "$run_output"
grep -F '"outlierCount":' <<< "$run_output"
grep -F '"confidenceIntervalLowerNanoseconds":' <<< "$run_output"
grep -F '"confidenceIntervalUpperNanoseconds":' <<< "$run_output"
grep -F '"confidenceLevelPercent": 99.9' <<< "$run_output"
if grep -Fq '"confidenceIntervalLowerNanoseconds": null' <<< "$run_output" || grep -Fq '"confidenceIntervalUpperNanoseconds": null' <<< "$run_output"; then
  echo "Normal BenchmarkDotNet run did not produce a usable confidence interval" >&2
  exit 1
fi

if grep -Fq '// BenchmarkDotNet' <<< "$run_output"; then
  echo "BenchmarkDotNet diagnostic output leaked into normalized evidence" >&2
  exit 1
fi


baseline_file="$(mktemp)"
candidate_file="$(mktemp)"
trap 'rm -f "$baseline_file" "$candidate_file"' EXIT

cat > "$baseline_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":100,"allocatedBytesPerOperation":1000,"statistics":{"sampleCount":15,"medianNanoseconds":100,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":99.9,"confidenceIntervalUpperNanoseconds":100.1,"confidenceLevelPercent":99.9}}]}
JSON
cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080,"statistics":{"sampleCount":15,"medianNanoseconds":104,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":103.9,"confidenceIntervalUpperNanoseconds":104.1,"confidenceLevelPercent":99.9}}]}
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
grep -F '"meanDecision":' <<< "$check_json"
grep -F '"status": "conclusiveWithinBudget"' <<< "$check_json"
grep -F '"sourceConfidenceLevelPercent": 99.9' <<< "$check_json"

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":106,"allocatedBytesPerOperation":1070,"statistics":{"sampleCount":15,"medianNanoseconds":106,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":105.9,"confidenceIntervalUpperNanoseconds":106.1,"confidenceLevelPercent":99.9}}]}
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
grep -F '"status": "conclusiveExceededBudget"' <<< "$failure_json"

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":106,"allocatedBytesPerOperation":1070,"statistics":{"sampleCount":15,"medianNanoseconds":106,"standardDeviationNanoseconds":2,"standardErrorNanoseconds":0.6,"outlierCount":1,"confidenceIntervalLowerNanoseconds":104,"confidenceIntervalUpperNanoseconds":108,"confidenceLevelPercent":99.9}}]}
JSON

set +e
uncertain_output="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10)"
uncertain_code=$?
set -e
test "$uncertain_code" -eq 2
grep -F "MapOrder: INCONCLUSIVE" <<< "$uncertain_output"
grep -F "regression bounds derived from BenchmarkDotNet 99.9% confidence intervals cross the configured budget of 5%" <<< "$uncertain_output"
grep -F "Overall: INCONCLUSIVE" <<< "$uncertain_output"

set +e
uncertain_json="$(dotnet run --project apps/cli/PerformanceAgent.Cli --configuration Release --no-build -- check "$baseline_file" "$candidate_file" 5 10 --format json)"
uncertain_json_code=$?
set -e
test "$uncertain_json_code" -eq 2
grep -F '"verdict": "inconclusive"' <<< "$uncertain_json"
grep -F '"status": "inconclusive"' <<< "$uncertain_json"
grep -F '"sourceConfidenceLevelPercent": 99.9' <<< "$uncertain_json"

cat > "$candidate_file" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":null,"statistics":{"sampleCount":15,"medianNanoseconds":104,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":103.9,"confidenceIntervalUpperNanoseconds":104.1,"confidenceLevelPercent":99.9}}]}
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
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080,"statistics":{"sampleCount":15,"medianNanoseconds":104,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":103.9,"confidenceIntervalUpperNanoseconds":104.1,"confidenceLevelPercent":99.9}}]}
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
{"runId":"run-temp","timestamp":"2026-09-30T12:00:00+00:00","commitSha":"abc123","evidence":{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":100,"allocatedBytesPerOperation":1000,"statistics":{"sampleCount":15,"medianNanoseconds":100,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":99.9,"confidenceIntervalUpperNanoseconds":100.1,"confidenceLevelPercent":99.9}}]}}
JSON
cat > "$temp_candidate" <<'JSON'
{"schemaVersion":"1.0","environment":{"runtime":".NET 10","operatingSystem":"Linux","architecture":"X64"},"measurements":[{"name":"MapOrder","meanNanoseconds":104,"allocatedBytesPerOperation":1080,"statistics":{"sampleCount":15,"medianNanoseconds":104,"standardDeviationNanoseconds":0.1,"standardErrorNanoseconds":0.03,"outlierCount":0,"confidenceIntervalLowerNanoseconds":103.9,"confidenceIntervalUpperNanoseconds":104.1,"confidenceLevelPercent":99.9}}]}
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