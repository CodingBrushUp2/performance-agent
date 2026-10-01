"""Real loopback HTTP checks against the CLI; no browser or third-party packages."""
import http.cookiejar
import json
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
    for run_id in ["run-first", "run-second"]:
        (archive / (run_id + ".json")).write_text(json.dumps({
            "runId": run_id, "timestamp": "2026-10-01T10:00:00+00:00", "commitSha": None,
            "evidence": {"schemaVersion": "1.0", "measurements": [
                {"name": "Smoke", "meanNanoseconds": 100, "allocatedBytesPerOperation": 0}]}
        }))
    original_archive = {p.name: p.read_bytes() for p in archive.iterdir()}
    with (work / "server.log").open("w+") as log:
        process = subprocess.Popen(["dotnet", str(cli), "ui", "--no-open"], cwd=work,
                                   stdout=log, stderr=subprocess.STDOUT)
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
            assert all(url.startswith("http://127.0.0.1:")
                       for url in re.findall(r"Now listening on: (\S+)", output)), output
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
            assert events_path.read_bytes() == before  # Refresh is read-only.
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
            # Corruption is reported, never silently treated as empty history.
            before = events_path.read_bytes()
            events_path.write_bytes(before + b"{broken\n")
            assert request()[0] == 409
            events_path.write_bytes(before)
            assert request()[0] == 200
            assert {p.name: p.read_bytes() for p in archive.iterdir()} == original_archive
            print("UI HTTP smoke passed: loopback, CSRF, selections, CLI parity, immutable evidence, errors.")
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
