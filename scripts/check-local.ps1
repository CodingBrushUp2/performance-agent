param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $RunId
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$candidate = Join-Path ([System.IO.Path]::GetTempPath()) ("perfagent-candidate-" + [Guid]::NewGuid().ToString("N") + ".json")
try {
    & ./.tools/perfagent run samples/QuickStartBenchmarks/QuickStartBenchmarks.csproj --output $candidate
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & ./.tools/perfagent check --run-id $RunId --candidate $candidate --budget samples/QuickStartBenchmarks/performance-budget.json
    exit $LASTEXITCODE
}
finally {
    Remove-Item $candidate -Force -ErrorAction SilentlyContinue
}
