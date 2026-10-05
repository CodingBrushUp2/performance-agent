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
    for run_id in ["run-first", "run-second", "run-'&é", "run-regression"]:
        (archive / (run_id + ".json")).write_text(json.dumps({
            "runId": run_id, "timestamp": "2026-10-01T10:00:00+00:00",
            "commitSha": None if run_id == "run-first" else "abc<script>sha</script>",
            "evidence": {"schemaVersion": "1.0",
                "environment": None if run_id == "run-first" else {
                    "runtime": "Runtime <script>runtime</script>", "operatingSystem": "OS & test", "architecture": "X64"},
                "measurements": [
                    {"name": "Smoke <script>name</script>",
                     "meanNanoseconds": 250.25 if run_id == "run-regression" else 100.125,
                     "allocatedBytesPerOperation": None if run_id == "run-first" else 0,
                     "statistics": {
                         "sampleCount": 15,
                         "medianNanoseconds": 250.25 if run_id == "run-regression" else 100.125,
                         "standardDeviationNanoseconds": 0.1,
                         "standardErrorNanoseconds": 0.03,
                         "outlierCount": 0,
                         "confidenceIntervalLowerNanoseconds": 250.15 if run_id == "run-regression" else 100.025,
                         "confidenceIntervalUpperNanoseconds": 250.35 if run_id == "run-regression" else 100.225
                     }}]}
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
            assert "Effective configuration" in page
            assert "Help / Getting Started" in page
            help_status, help_page, _ = request("/help")
            assert help_status == 200 and "AI proposes. Measurements decide." in help_page
            assert "perfagent analyze &lt;candidate-run-id&gt;" in help_page
            config_path = work / "perfagent.json"
            config_before = config_path.read_bytes() if config_path.exists() else None
            for document, source, mean, allocation in [
                (None, "Built-in defaults (file absent)", "5", "5"),
                ({}, "Built-in defaults (budget absent)", "5", "5"),
                ({"budget": {"maxMeanRegressionPercent": 12.5}, "apiKey": "MUST-NOT-BE-DISPLAYED"}, "perfagent.json", "12.5", "Not configured")]:
                if document is None:
                    config_path.unlink(missing_ok=True)
                else:
                    config_path.write_text(json.dumps(document))
                status, config_page, _ = request("/configuration")
                config_cli = run_cli(work, "config", "show")
                assert status == 200 and source in config_page and source in config_cli
                assert f"<dd>{mean}</dd>" in config_page and f"<dd>{allocation}</dd>" in config_page
                assert f"Max mean regression (%): {mean}" in config_cli
                assert f"Max allocation regression (%): {allocation}" in config_cli
                assert "MUST-NOT-BE-DISPLAYED" not in config_page + config_cli
                assert str(config_path) in config_page and str(config_path) in config_cli
                assert "<dt>AI provider</dt><dd>openai</dd>" in config_page and "AI provider: openai" in config_cli
                assert "<dt>AI model</dt><dd>Not configured</dd>" in config_page and "AI model: Not configured" in config_cli
            for document in ['{broken', 'null', '{"budget":{"maxMeanRegressionPercent":-1}}']:
                config_path.write_text(document)
                assert request("/configuration")[0] == 409
                bad = subprocess.run(["dotnet", str(cli), "config", "show"], cwd=work,
                                     capture_output=True, text=True, timeout=30)
                assert bad.returncode == 2 and "perfagent.json" in bad.stderr
            if config_before is None:
                config_path.unlink()
            else:
                config_path.write_bytes(config_before)
            assert request("/configuration")[0] == 200
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
            status, no_baseline, _ = request("/runs/run-second/report")
            assert status == 409 and "perfagent baseline set" in no_baseline

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
            current_row = page[page.index("<code>run-second</code>"):page.index("</tr>", page.index("<code>run-second</code>"))]
            anchor_row = page[page.index("<code>run-first</code>"):page.index("</tr>", page.index("<code>run-first</code>"))]
            assert "Make Current" not in current_row
            assert "Make Anchor" not in anchor_row
            assert events_path.read_bytes() == before  # Refresh is read-only.
            status, report_page, report_headers = request("/runs/run-second/report")
            assert status == 200 and "<h2>PASS</h2>" in report_page
            assert "attachment;" in report_headers["Content-Disposition"]
            cli_report = run_cli(work, "report", "run-second")
            normalize = lambda text: re.sub(r"Generated: [^<]+", "Generated: timestamp", text)
            assert normalize(report_page) == normalize(cli_report)
            assert "<script>" not in report_page and "&lt;script&gt;name&lt;/script&gt;" in report_page
            assert "Environment validation: compatible" in report_page
            assert "Measured baseline" in report_page and "Derived change (%)" in report_page
            assert " href=" not in report_page and " src=" not in report_page
            failed_report = subprocess.run(["dotnet", str(cli), "report", "run-regression"], cwd=work,
                                           text=True, capture_output=True, timeout=30)
            assert failed_report.returncode == 1 and "<h2>REGRESSION</h2>" in failed_report.stdout
            assert "<h2>REGRESSION</h2>" in request("/runs/run-regression/report")[1]
            override_budget = work / "report-budget.json"
            override_budget.write_text('{"maxMeanRegressionPercent":200}')
            overridden = run_cli(work, "report", "run-regression", "--baseline", "run-second", "--budget", str(override_budget))
            assert "<h2>PASS</h2>" in overridden and str(override_budget) in overridden
            for run_id in ["run-first", "run-missing"]:
                invalid_report = subprocess.run(["dotnet", str(cli), "report", run_id], cwd=work,
                                                text=True, capture_output=True, timeout=30)
                assert invalid_report.returncode == 2 and not invalid_report.stdout
                assert request("/runs/" + run_id + "/report")[0] == 409
            numeric_report = run_cli(work, "compare", "Numeric", "100", "90", "0", "64", "html")
            assert "No budget check or environment validation was requested" in numeric_report
            assert "<td>NoBaseline</td>" in numeric_report
            assert events_path.read_bytes() == before
            status, regression, _ = request("/runs/run-second/check-current")
            assert status == 200 and "<h1>PASS</h1>" in regression
            assert "Candidate <code>run-second</code> vs Current <code>run-second</code>" in regression
            assert "Budget: mean +5%, allocation +5%" in regression
            assert "Mean baseline (ns)" in regression and "Mean candidate (ns)" in regression
            assert "Allocation baseline (B/op)" in regression and "Allocation candidate (B/op)" in regression
            assert regression.count(">100.125</td>") >= 2 and regression.count(">0</td>") >= 2
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

            # AI analysis is an explicit, CSRF-protected POST through the same use case as `perfagent analyze`.
            # Without ai.model (and no OPENAI_API_KEY; OpenAI is never contacted) the measured result is still
            # shown, only the AI part fails, and workspace state stays byte-identical.
            state_before = {p: p.read_bytes() for p in state.rglob("*") if p.is_file()}
            status, details, _ = request("/runs/run-regression")
            assert status == 200 and '<form method="post" action="/runs/run-regression/analyze" data-analysis-form>' in details
            assert 'data-analysis-button>Analyze with AI</button>' in details
            assert "data-analysis-status hidden" in details
            assert "Analyzing with the configured provider" in details
            assert 'src="/assets/ui.js"' in details
            script_status, script_body, _ = request("/assets/ui.js")
            assert script_status == 200 and "button.disabled = true" in script_body
            assert 'button.textContent = "Analyzing..."' in script_body
            assert "<dt>Deterministic result</dt><dd><strong>REGRESSION</strong></dd>" in details
            assert request("/runs/run-regression/analyze")[0] == 405
            assert request("/runs/run-regression/analyze", {"x": "y"})[0] == 400
            analyze_form = {"__RequestVerificationToken": re.search(
                r'name="__RequestVerificationToken" value="([^"]+)"', details)[1]}
            assert request("/runs/run-regression/analyze", analyze_form, {"Origin": "https://evil.example"})[0] == 403
            assert request("/runs/run-regression/analyze", analyze_form, {"Sec-Fetch-Site": "cross-site"})[0] == 403
            status, analysis_page, _ = request("/runs/run-regression/analyze", analyze_form)
            assert status == 200 and '<p class="verdict">REGRESSION</p>' in analysis_page
            assert "<dt>Current baseline</dt><dd><code>run-second</code></dd>" in analysis_page
            assert "AI analysis failed" in analysis_page and "&quot;ai.model&quot;" in analysis_page
            assert "Only the AI analysis failed" in analysis_page
            assert "<script>" not in analysis_page and "&lt;script&gt;name&lt;/script&gt;" in analysis_page
            cli_environment = {k: v for k, v in os.environ.items() if k != "OPENAI_API_KEY"}
            cli_analyze = subprocess.run(["dotnet", str(cli), "analyze", "run-regression"], cwd=work, text=True,
                                         capture_output=True, timeout=30, env=cli_environment)
            assert cli_analyze.returncode == 2 and "Deterministic result: REGRESSION" in cli_analyze.stdout
            assert "Baseline:  run-second (Current)" in cli_analyze.stdout
            assert {p: p.read_bytes() for p in state.rglob("*") if p.is_file()} == state_before
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
