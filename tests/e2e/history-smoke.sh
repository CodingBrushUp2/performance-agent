#!/usr/bin/env bash
set -euo pipefail
repo_root="$PWD"
cli="$repo_root/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cd "$work"

dotnet "$cli" history > empty.txt
grep -Fx 'No archived benchmark runs.' empty.txt
grep -Fx 'No baseline events.' empty.txt
grep -Fx 'Active Current: -' empty.txt
mkdir -p .performance-agent/archive
python3 - <<'PY'
import json
for name in ['run-first', 'run-second']:
    evidence = {'schemaVersion':'1.0','environment':{'runtime':'.NET 10','operatingSystem':'Linux','architecture':'X64'},'measurements':[{'name':'Smoke','meanNanoseconds':100,'allocatedBytesPerOperation':0,'statistics':{'sampleCount':15,'medianNanoseconds':100,'standardDeviationNanoseconds':0.1,'standardErrorNanoseconds':0.03,'outlierCount':0,'confidenceIntervalLowerNanoseconds':99.9,'confidenceIntervalUpperNanoseconds':100.1}}]}
    with open('.performance-agent/archive/' + name + '.json', 'w') as f:
        json.dump({'runId':name,'timestamp':'2026-09-30T12:00:00+00:00','commitSha':None,'evidence':evidence}, f)
with open('candidate.json','w') as f: json.dump(evidence,f)
PY
dotnet "$cli" baseline set run-first
dotnet "$cli" baseline anchor run-first
dotnet "$cli" baseline set run-second
dotnet "$cli" history > history.txt
grep -F 'Current Created  - -> run-first' history.txt
grep -F 'Anchor Created  - -> run-first' history.txt
grep -F 'Current Reset  run-first -> run-second' history.txt
grep -Fx 'Active Current: run-second' history.txt
grep -Fx 'Active Anchor: run-first' history.txt
grep -F '[current]' history.txt
grep -F '[anchor]' history.txt
dotnet "$cli" history > repeat.txt
cmp history.txt repeat.txt

# Invalid runs must not create events or change pointers.
cp .performance-agent/baseline-events.jsonl before.jsonl
if dotnet "$cli" baseline set run-missing; then exit 1; fi
cmp before.jsonl .performance-agent/baseline-events.jsonl

# Append failure must leave the pointer alone.
mv .performance-agent/baseline-events.jsonl saved.jsonl
mkdir .performance-agent/baseline-events.jsonl
cp .performance-agent/baselines/current.json before-pointer.json
if dotnet "$cli" baseline set run-first; then exit 1; fi
cmp before-pointer.json .performance-agent/baselines/current.json
rmdir .performance-agent/baseline-events.jsonl
mv saved.jsonl .performance-agent/baseline-events.jsonl

# Simulate pointer write failure after a committed event.
rm .performance-agent/baselines/current.json
mkdir .performance-agent/baselines/current.json
if dotnet "$cli" baseline set run-first; then exit 1; fi
dotnet "$cli" history > recovered.txt
grep -F 'Current Reset  run-second -> run-first' recovered.txt
grep -Fx 'Active Current: run-first' recovered.txt
dotnet "$cli" check --candidate candidate.json --budget "$repo_root/samples/ci/performance-budget.json" | grep -Fx 'Overall: PASS'

# Promotion events from other callers are also visible; equal timestamps retain append order.
python3 - <<'PY'
import json
path='.performance-agent/baseline-events.jsonl'
with open(path) as f: events=[json.loads(line) for line in f]
with open(path,'a') as f:
    f.write(json.dumps({'eventId':'event-promoted','timestamp':events[-1]['timestamp'],'kind':1,'type':1,'runId':'run-second','previousRunId':'run-first','reason':'Accepted change'})+'\n')
PY
dotnet "$cli" history > promoted.txt
grep -F 'Current Promoted  run-first -> run-second' promoted.txt
grep -Fx 'Active Current: run-second' promoted.txt
printf '{broken\n' >> .performance-agent/baseline-events.jsonl
if dotnet "$cli" history > invalid.txt 2> error.txt; then exit 1; fi
grep -F 'Baseline event history contains invalid JSON.' error.txt
