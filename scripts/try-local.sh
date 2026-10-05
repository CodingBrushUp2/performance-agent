#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

rm -rf ./.tools ./artifacts
dotnet build PerformanceAgent.sln -c Release
dotnet pack apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj -c Release --no-build -o artifacts
dotnet tool install --tool-path ./.tools PerformanceAgent.Cli --version 0.2.0 --add-source ./artifacts

echo
echo "== Calibrating quick-start benchmark =="
./.tools/perfagent calibrate samples/QuickStartBenchmarks/QuickStartBenchmarks.csproj

echo
echo "== Archived history =="
./.tools/perfagent history

echo
echo "Operational demo complete."
echo "Choose a reported RunId explicitly when you are ready:"
echo "  ./.tools/perfagent baseline set <run-id>"
echo "  ./.tools/perfagent baseline anchor <run-id>"
