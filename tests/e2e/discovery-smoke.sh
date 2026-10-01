#!/usr/bin/env bash
set -euo pipefail
host="$PWD/apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/benchmark-host/PerformanceAgent.BenchmarkHost.dll"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
expect_failure() {
  set +e
  dotnet "$host" "$1" "$work/evidence.json" > "$work/stdout" 2> "$work/stderr"
  code=$?
  set -e
  cat "$work/stderr"
  test "$code" -eq 2
  grep -F "$2" "$work/stderr"
  test ! -e "$work/evidence.json"
  if grep -Fq 'Unhandled exception' "$work/stderr"; then exit 1; fi
}
expect_failure "$work/missing.dll" 'Benchmark assembly not found'
printf 'not an assembly' > "$work/invalid.dll"
expect_failure "$work/invalid.dll" 'Cannot load benchmark assembly'
mkdir "$work/Dependency" "$work/Consumer"
cat > "$work/Dependency/MissingDependency.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
XML
printf 'public class DependencyBase {}\n' > "$work/Dependency/Base.cs"
cat > "$work/Consumer/Consumer.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Dependency/MissingDependency.csproj" /></ItemGroup>
</Project>
XML
printf 'public class Unloadable : DependencyBase {}\n' > "$work/Consumer/Consumer.cs"
dotnet build "$work/Consumer/Consumer.csproj" -c Release
rm "$work/Consumer/bin/Release/net10.0/MissingDependency.dll"
expect_failure "$work/Consumer/bin/Release/net10.0/Consumer.dll" 'No loadable BenchmarkDotNet benchmark types remain.'
grep -F 'MissingDependency' "$work/stderr"
grep -F 'Restore dependencies' "$work/stderr"
