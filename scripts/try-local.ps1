$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Remove-Item ./.tools, ./artifacts -Recurse -Force -ErrorAction SilentlyContinue
dotnet build PerformanceAgent.sln -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet pack apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj -c Release --no-build -o artifacts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet tool install --tool-path ./.tools PerformanceAgent.Cli --version 0.1.0 --add-source ./artifacts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "== Calibrating quick-start benchmark =="
& ./.tools/perfagent calibrate samples/QuickStartBenchmarks/QuickStartBenchmarks.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "== Archived history =="
& ./.tools/perfagent history
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Operational demo complete."
Write-Host "Choose a reported RunId explicitly when you are ready:"
Write-Host "  ./.tools/perfagent baseline set <run-id>"
Write-Host "  ./.tools/perfagent baseline anchor <run-id>"
