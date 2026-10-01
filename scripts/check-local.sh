#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: bash scripts/check-local.sh <run-id>" >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

run_id="$1"
candidate="$(mktemp)"
trap 'rm -f "$candidate"' EXIT

./.tools/perfagent run samples/QuickStartBenchmarks/QuickStartBenchmarks.csproj --output "$candidate"
./.tools/perfagent check --run-id "$run_id" --candidate "$candidate" --budget samples/QuickStartBenchmarks/performance-budget.json
