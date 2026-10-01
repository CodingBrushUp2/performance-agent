"""Real loopback HTTP checks against the CLI; no browser or third-party packages."""
import http.cookiejar
import json
import html
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request


repo = Path(__file__).resolve().parents[2]
cli = repo / "apps/cli/PerformanceAgent.Cli/bin/Release/net10.0/perfagent.dll"


def run_cli(work, *args):
    return subprocess.run(["dotnet", str(cli), *args], cwd=work, text=True,
                          capture_output=True, check=True, timeout=30).stdout


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


with tempfile.TemporaryDirectory(prefix="perfagent-ui-") as directory:
    work = Path(directory)
    state = work / ".performance-agent"
    archive = state / "archive"
    archive.mkdir(parents=True)
    for run_id in ["run-first", "run-second", "run-'&é"]:
        (archive / (run_id + ".json")).write_text(json.dumps({
            "runId": run_id, "timestamp": "2026-10-01T10:00:00+00:00",
            "commitSha": None if run_id == "run-first" else "abc<script>sha</script>",
            "evidence": {"schemaVersion": "1.0",
                "environment": None if run_id == "run-first" else {
                    "runtime": "Runtime <script>runtime</script>", "operatingSystem": "OS & test", "architecture": "X64"},
                "measurements": [
                    {"name": "Smoke <script>name</script>", "meanNanoseconds": 100.125,
                     "allocatedBytesPerOperation": None if run_id == "run-first" else 0}]}
        }))
    original_archive = {p.name: p.read_bytes() for p in archive.iterdir()}
    # Workspace applications and inherited ASP.NET settings cannot widen UI binding.
    (work / "appsettings.json").write_text(json.dumps({
        "Kestrel": {"Endpoints": {"Workspace": {"Url": "http://0.0.0.0:0"}}}}))
    server_environment = dict(os.environ, ASPNETCORE_URLS="http://0.0.0.0:0",
                              Kestrel__Endpoints__Environment__Url="http://0.0.0.0:0")
    with (work / "server.log").open("w+") as log:
        process = subprocess.Popen(["dotnet", str(cli), "ui", "--no-open"], cwd=work,
                                   stdout=log, stderr=subprocess.STDOUT, env=server_environment)
        try:
            deadline = time.monotonic() + 30
            address = None
            while time.monotonic() < deadline:
                log.seek(0)
                output = log.read()
                match = re.search(r"Performance Agent UI: (http://127\.0\.0\.1:\d+)", output)
                if match:
                    address = match[1]
                    break
                assert process.poll() is None, output
                time.sleep(0.05)
            assert address, output
            # Check the announced address and every Kestrel listener.
            assert re.findall(r"Now listening on: (\S+)", output) == [address], output
            opener = urllib.request.build_opener(
                urllib.request.ProxyHandler({}),
                urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), NoRedirect())

            def request(path="/", data=None, headers=None):
                body = None if data is None else urllib.parse.urlencode(data).encode()
                req = urllib.request.Request(address + path, data=body, headers=headers or {})
                try:
                    response = opener.open(req, timeout=10)
                except urllib.error.HTTPError as error:
                    response = error
                with response:
                    return response.status, response.read().decode(), response.headers

            status, page, headers = request()
            assert status == 200 and "Make Current" in page and "Make Anchor" in page
            assert "View Details" in page
            assert str(work) in page and str(state) in page and "<dd>Yes</dd>" in page
            assert "Admin</dt><dd>Not required" in page
            storage_output = run_cli(work, "storage")
            assert f"Workspace: {work}" in storage_output and f"Storage: {state}" in storage_output
            assert "Writable: Yes" in storage_output and "Admin: Not required" in storage_output
            assert not list(state.glob(".write-probe-*"))
            assert "no-store" in headers["Cache-Control"]
            assert "frame-ancestors 'none'" in headers["Content-Security-Policy"]
            token = re.search(r'name="__RequestVerificationToken" value="([^"]+)"', page)[1]
            form = {"runId": "run-first", "__RequestVerificationToken": token}
            assert request("/baselines/current")[0] == 405
            assert request("/baselines/current", {"runId": "run-first"})[0] == 400
            assert request("/baselines/current", form, {"Origin": "https://evil.example"})[0] == 403
            assert request("/baselines/current", form, {"Origin": "null"})[0] == 403
            assert request("/baselines/current", form, {"Sec-Fetch-Site": "cross-site"})[0] == 403
            assert request(headers={"Host": "evil.example"})[0] == 403
            for bad_id, expected in [("../outside", 400), ("", 400), ("run-missing", 404)]:
                assert request("/baselines/current", dict(form, runId=bad_id))[0] == expected
            assert not (state / "baseline-events.jsonl").exists()

            status, _, headers = request("/baselines/current", form, {"Origin": address})
            assert status == 303 and headers["Location"] == "/"
            assert request("/baselines/anchor", form)[0] == 303
            assert request("/baselines/current", dict(form, runId="run-second"))[0] == 303
            events_path = state / "baseline-events.jsonl"
            events = [json.loads(line) for line in events_path.read_text().splitlines()]
            assert [(e["kind"], e["type"], e["runId"], e["previousRunId"]) for e in events] == [
                (1, 0, "run-first", None), (0, 0, "run-first", None),
                (1, 2, "run-second", "run-first")]
            history = run_cli(work, "history")
            assert "Active Current: run-second" in history and "Active Anchor: run-first" in history
            assert json.loads((state / "baselines/current.json").read_text())["runId"] == "run-second"
            assert json.loads((state / "baselines/anchor.json").read_text())["runId"] == "run-first"
            before = events_path.read_bytes()
            page = request()[1]
            assert "<strong>run-second</strong>" in page and "<strong>run-first</strong>" in page
            assert "Baseline timeline" in page and "run-first</code> → <code>run-second" in page
            assert events_path.read_bytes() == before  # Refresh is read-only.
            status, regression, _ = request("/runs/run-second/check-current")
            assert status == 200 and "<h1>PASS</h1>" in regression
            assert "Candidate <code>run-second</code> vs Current <code>run-second</code>" in regression
            assert "Budget: mean +5%, allocation +5%" in regression\n            assert "Mean baseline (ns)" in regression and "Mean candidate (ns)" in regression\n            assert "Allocation baseline (B/op)" in regression and "Allocation candidate (B/op)" in regression\n            assert regression.count(">100.125</td>") >= 2 and regression.count(">0</td>") >= 2
            # Current-vs-Current has a 0% allocation change; formatting of unavailable values is covered by run details.
            assert "Unavailable%" not in regression
            for run_id in ["run-first", "run-second", "run-'&é"]:
                status, details, _ = request("/runs/" + urllib.parse.quote(run_id, safe=""))
                assert status == 200 and run_id in html.unescape(details)
                assert "2026-10-01T10:00:00.0000000+00:00" in details and "100.125" in details
                assert "Mean (ns)" in details and "Allocation (B/op)" in details
                assert "<script>" not in details and "&lt;script&gt;name&lt;/script&gt;" in details
                output = run_cli(work, "history", run_id)
                assert f"RunId: {run_id}" in output and "Mean (ns): 100.125" in output
                if run_id == "run-first":
                    assert details.count("<dd>Unavailable</dd>") == 4
                    assert "<td>Unavailable</td>" in details
                    assert '<span class="badge">Anchor</span>' in details
                    assert "Allocation (B/op): Unavailable" in output
                else:
                    assert "abc&lt;script&gt;sha&lt;/script&gt;" in details
                    assert "Runtime &lt;script&gt;runtime&lt;/script&gt;" in details
                    assert "OS &amp; test" in details and "X64" in details
                    assert "Allocation (B/op): 0" in output
                if run_id == "run-second":
                    assert '<span class="badge">Current</span>' in details
                    assert "Current: True; Anchor: False" in output
            assert events_path.read_bytes() == before  # Details never change history.
            assert request("/runs/run-missing")[0] == 404
            assert request("/runs/bad%22id")[0] == 400
            missing = subprocess.run(["dotnet", str(cli), "history", "run-missing"], cwd=work,
                                     text=True, capture_output=True, timeout=30)
            assert missing.returncode == 2 and "not found" in missing.stderr
            run_cli(work, "baseline", "anchor", "run-second")
            assert request()[1].count("<strong>run-second</strong>") == 2

            # A committed event remains authoritative when the derived pointer write fails.
            pointer = state / "baselines/current.json"
            pointer.unlink()
            pointer.mkdir()
            status, error, headers = request("/baselines/current", form)
            assert status == 409 and "Reload history" in error
            assert headers["Content-Type"].startswith("text/plain")
            assert "<strong>run-first</strong>" in request()[1]
            assert "Active Current: run-first" in run_cli(work, "history")
            pointer.rmdir()
            # Status uses the same storage boundary in CLI and UI. No silent relocation.
            saved_state = work / "saved-state"
            state.rename(saved_state)
            state.write_text("A file prevents creating the state directory")
            status, page, _ = request()
            assert status == 200 and "<dd>No</dd>" in page and "Fix the folder permissions" in page
            assert str(state) in page and "Make Current" not in page
            blocked = subprocess.run(["dotnet", str(cli), "storage"], cwd=work,
                                     text=True, capture_output=True, timeout=30)
            assert blocked.returncode == 2 and "Writable: No" in blocked.stdout
            assert "Administrator/root privileges are not required" in blocked.stderr
            assert request("/baselines/current", form)[0] == 409
            assert state.is_file()
            state.unlink()
            saved_state.rename(state)
            if os.name == "posix" and os.geteuid() != 0:
                mode = state.stat().st_mode
                try:
                    state.chmod(0o555)
                    status, page, _ = request()
                    assert status == 200 and "<dd>No</dd>" in page
                    assert "Make Current" not in page and "Fix the folder permissions" in page
                    assert request("/baselines/current", form)[0] == 409
                finally:
                    state.chmod(mode)
            # Corruption is reported, never silently treated as empty history.
            before = events_path.read_bytes()
            events_path.write_bytes(before + b"{broken\n")
            assert request()[0] == 409
            events_path.write_bytes(before)
            assert request()[0] == 200
            assert {p.name: p.read_bytes() for p in archive.iterdir()} == original_archive
            print("UI HTTP smoke passed: loopback, CSRF, selections, details, storage, CLI parity, immutable evidence, errors.")
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
            log.seek(0)
            if process.returncode not in (0, -15):
                print(log.read())
