"""Exercise real CLI SIGINT handling with controlled dotnet child process trees (Linux CI)."""
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time

repo = Path.cwd()
cli = repo / "apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll"
dotnet = shutil.which("dotnet")
assert dotnet

stub = r'''#!/usr/bin/env python3
import json, os, pathlib, subprocess, sys, time
args=sys.argv[1:]
phase="query" if args[0]=="msbuild" else "build" if args[0]=="build" else "benchmark"
if phase != os.environ["PERF_TEST_PHASE"]:
    if phase=="query": print(str(pathlib.Path("Benchmark.dll").resolve()))
    sys.exit(0)
state=pathlib.Path(os.environ["PERF_TEST_STATE"])
child=subprocess.Popen([sys.executable, "-c", "import subprocess,sys,time,pathlib; p=subprocess.Popen([sys.executable,'-c','import time; time.sleep(300)']); pathlib.Path(sys.argv[1]).write_text(str(p.pid)); time.sleep(300)", str(state)+".grandchild"])
evidence=args[2] if phase=="benchmark" else None
if evidence: pathlib.Path(evidence).write_text("incomplete evidence")
state.write_text(json.dumps({"pid":os.getpid(),"child":child.pid,"evidence":evidence}))
print("child ready", flush=True)
print("diagnostic pipe open", file=sys.stderr, flush=True)
time.sleep(300)
'''


def running(pid):
    stat = Path(f"/proc/{pid}/stat")
    if not stat.exists():
        return False
    try:
        return stat.read_text().rsplit(")", 1)[1].split()[0] not in ("Z", "X")
    except FileNotFoundError:
        return False


for phase in ("query", "build", "benchmark"):
    with tempfile.TemporaryDirectory(prefix="perf-cancel-") as directory:
        work = Path(directory)
        tools = work / "tools"
        tools.mkdir()
        executable = tools / "dotnet"
        executable.write_text(stub)
        executable.chmod(0o755)
        project = work / "Benchmark.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />')
        state = work / "state.json"
        env = os.environ | {"PATH": str(tools) + os.pathsep + os.environ["PATH"],
                            "PERF_TEST_PHASE": phase, "PERF_TEST_STATE": str(state)}
        process = subprocess.Popen([str(cli.with_suffix("")), "run", str(project), "--output", str(work / "evidence.json")],
                                   cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                                   start_new_session=True)
        pids = []
        try:
            deadline = time.monotonic() + 20
            while not (state.exists() and Path(str(state) + ".grandchild").exists()):
                assert process.poll() is None, (phase, process.returncode, process.communicate())
                assert time.monotonic() < deadline, f"{phase}: child did not become ready"
                time.sleep(0.05)
            data = json.loads(state.read_text())
            pids = [data["pid"], data["child"], int(Path(str(state) + ".grandchild").read_text())]
            # Signal only perfagent, so the test cannot pass through terminal group signalling.
            os.kill(process.pid, signal.SIGINT)
            stdout, stderr = process.communicate(timeout=15)
            assert process.returncode == 130, (phase, process.returncode, stdout, stderr)
            assert "Benchmark run cancelled." in stderr, stderr
            assert "Unhandled exception" not in stderr, stderr
            deadline = time.monotonic() + 5
            while any(running(pid) for pid in pids) and time.monotonic() < deadline:
                time.sleep(0.05)
            assert not any(running(pid) for pid in pids), (phase, "orphan process", pids)
            assert not (work / "evidence.json").exists()
            assert not (work / ".performance-agent/archive").exists()
            if data["evidence"]:
                assert not Path(data["evidence"]).exists(), "Temporary host evidence leaked"
            print(f"Ctrl+C during {phase}: exit 130, descendants stopped, no evidence published.")
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            for pid in pids:
                if running(pid):
                    os.kill(pid, signal.SIGKILL)
