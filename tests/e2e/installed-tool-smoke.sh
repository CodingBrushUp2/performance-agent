#!/usr/bin/env bash
set -euo pipefail
repo_root="$PWD"
work="$(mktemp -d)"
cleanup() {
  result=$?
  if [ "$result" -ne 0 ]; then
    for log in "$work/external consumer/run.stdout" "$work/external consumer/run.stderr"; do
      if [ -f "$log" ]; then cat "$log" >&2; fi
    done
  fi
  rm -rf "$work"
  exit "$result"
}
trap cleanup EXIT

# The solution must be built first, as in CI. Never publish this package.
dotnet pack "$repo_root/apps/cli/PerformanceAgent.Cli/PerformanceAgent.Cli.csproj" \
  --configuration Release --no-build --output "$work/feed"

python3 - "$work/feed/PerformanceAgent.Cli.0.1.0.nupkg" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as package:
    names=set(package.namelist())
    prefix='tools/net10.0/any/benchmark-host/'
    for name in ['PerformanceAgent.BenchmarkHost.dll', 'PerformanceAgent.BenchmarkHost.deps.json',
                 'PerformanceAgent.BenchmarkHost.runtimeconfig.json', 'PerformanceAgent.BenchmarkDotNet.dll',
                 'PerformanceAgent.Core.dll', 'BenchmarkDotNet.dll']:
        assert prefix+name in names, name
    assert not any(name.endswith('.csproj') for name in names)
PY

# An isolated manifest and package cache prevent a previously installed 0.1.0 masking defects.
export DOTNET_CLI_HOME="$work/cli-home"
export NUGET_PACKAGES="$work/packages"
export DOTNET_NOLOGO=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
mkdir -p "$work/external consumer/Benchmarks" "$work/external consumer/Helper"
cd "$work/external consumer"
dotnet new tool-manifest
cat > "$work/tool-feed.config" <<XML
<configuration><packageSources><clear /><add key="local" value="$work/feed" /></packageSources></configuration>
XML
dotnet tool install PerformanceAgent.Cli --local --version 0.1.0 --configfile "$work/tool-feed.config"
dotnet tool run perfagent -- compare ToolSmoke 100 90 1000 900 markdown | grep -F '| ToolSmoke | Mean (ns) | 100 | 90 | -10% | Comparable |'

cat > Helper/Helper.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
XML
cat > Helper/Work.cs <<'CS'
namespace ExternalDependency;
public static class Work
{
    public static byte[] Allocate() => new byte[64];
}
CS
cat > Benchmarks/Benchmarks.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.15.8" />
    <ProjectReference Include="../Helper/Helper.csproj" />
  </ItemGroup>
</Project>
XML
cat > Benchmarks/Program.cs <<'CS'
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ExternalDependency;

BenchmarkSwitcher.FromAssembly(typeof(InstalledBenchmark).Assembly).Run(args);

// Smoke-only configuration. The host continues to use normal BDN defaults.
[DryJob]
[MemoryDiagnoser]
public class InstalledBenchmark
{
    [Benchmark]
    public byte[] Allocate() => Work.Allocate();
}
CS

# Both tool installation and consumer project are outside the Performance Agent tree.
timeout 180s dotnet tool run perfagent -- run "$PWD/Benchmarks/Benchmarks.csproj" \
  --output "$PWD/evidence.json" > run.stdout 2> run.stderr
python3 - <<'PY'
import json, math, pathlib
with open('evidence.json') as f: evidence=json.load(f)
assert evidence['schemaVersion']=='1.0'
measurement, = evidence['measurements']
assert measurement['name']=='InstalledBenchmark.Allocate'
assert math.isfinite(measurement['meanNanoseconds']) and measurement['meanNanoseconds'] > 0
assert measurement['allocatedBytesPerOperation'] >= 64
assert all(evidence['environment'][key] for key in ['runtime','operatingSystem','architecture'])
archive, = pathlib.Path('.performance-agent/archive').glob('run-*.json')
with archive.open() as f: archived=json.load(f)
assert archived['evidence']==evidence
assert archived['runId']==archive.stem
assert '// BenchmarkDotNet' not in pathlib.Path('run.stdout').read_text()
print('Installed tool produced valid measured evidence and an archived run.')
PY

dotnet tool run perfagent -- history | grep -F 'No baseline events.'

# Calibration uses repeated real tool executions but must never select a baseline implicitly.
timeout 180s dotnet tool run perfagent -- calibrate "$PWD/Benchmarks/Benchmarks.csproj" \
  --runs 2 --max-spread 100000 > calibrate.stdout 2> calibrate.stderr
grep -F 'Calibration: STABLE' calibrate.stdout
grep -F 'Baselines were not changed.' calibrate.stdout
dotnet tool run perfagent -- history > calibration-history.stdout
grep -F 'Active Current: -' calibration-history.stdout
grep -F 'Active Anchor: -' calibration-history.stdout
archive_count="$(find .performance-agent/archive -maxdepth 1 -name '*.json' | wc -l)"
test "$archive_count" -eq 3


# The bundled local UI must start from the installed tool, bind to loopback, and serve its root page.
dotnet tool run perfagent -- ui --no-open > ui.stdout 2> ui.stderr &
ui_pid=$!
ui_address=""
for _ in {1..50}; do
  if grep -q 'Performance Agent UI:' ui.stdout 2>/dev/null; then
    ui_address="$(sed -n 's/^Performance Agent UI: //p' ui.stdout | head -n1)"
    break
  fi
  sleep 0.2
done
test -n "$ui_address"
case "$ui_address" in http://127.0.0.1:*) ;; *) echo "UI did not bind to loopback: $ui_address" >&2; kill "$ui_pid" || true; exit 1;; esac
python3 - "$ui_address" <<'PY'
import sys, urllib.request
with urllib.request.urlopen(sys.argv[1], timeout=5) as response:
    body=response.read().decode()
assert response.status == 200
assert '<title>Performance Agent</title>' in body
assert 'Benchmark history' in body
PY
kill "$ui_pid"
wait "$ui_pid" || true

# A damaged installation fails clearly; it must not search for a source-tree host.
rm "$NUGET_PACKAGES/performanceagent.cli/0.1.0/tools/net10.0/any/benchmark-host/PerformanceAgent.BenchmarkHost.dll"
if dotnet tool run perfagent -- run "$PWD/Benchmarks/Benchmarks.csproj" --output missing.json > missing.stdout 2> missing.stderr; then
  echo 'Run unexpectedly succeeded without its bundled host.' >&2
  exit 1
fi
grep -F 'The bundled Performance Agent benchmark host is missing.' missing.stderr
test ! -e missing.json
