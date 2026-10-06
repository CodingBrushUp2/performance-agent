# CI execution policy

Routine pull requests targeting `main` restore and build the solution and run the three unit-test projects. Draft PRs skip the runner job; marking a PR ready runs it. New pushes cancel superseded runs. There is no automatic `push main` run after merge.

Use **Actions > CI > Run workflow** on the intended branch for full validation. That execution also runs both integration-test projects, all six CLI/evidence/cancellation/UI smoke scripts, package smoke checks, and the installed-tool test outside the source tree. Run this before a release and for changes to packaging, benchmark execution, discovery, cancellation, storage, or the local UI. The manual workflow must already exist on the default branch.

**Performance Gate Demo** is manual only. It checks fixed JSON fixtures; it is not a new performance measurement and does not need to run on every PR. It builds the CLI project rather than the whole solution.

| Event | Automatic work |
| --- | --- |
| Ready PR opened or updated | Restore, solution build, unit tests |
| Draft PR updated | Job skipped |
| Merge or direct push to main | No automatic run |
| CI manually dispatched | Unit tests, integration tests, all smoke/package checks |
| Performance Gate Demo manually dispatched | Fixed-fixture demo |

Direct pushes to main bypass PR validation. Use PRs for changes. Keep the `test` job name stable if it is configured as a required check; do not require the manual demo. No branch-protection rules were changed by this update.

Finish and validate a coherent phase locally, then open one PR. Avoid repeated pushes while a phase is still being assembled. Dependabot groups routine updates by ecosystem to reduce separate PRs; security updates may still be separate.

Local verification without Actions minutes:

```bash
dotnet restore PerformanceAgent.sln
dotnet build PerformanceAgent.sln --configuration Release --no-restore
for project in tests/unit/*/*.csproj; do
  dotnet test "$project" --configuration Release --no-build --no-restore
done
```

For full local verification, additionally run both integration projects and the commands listed in `.github/workflows/ci.yml`. Existing end-to-end scripts expect the solution to be built first.

GitHub-hosted manual runs still consume the account's applicable quota. This policy reduces future work; it does not restore exhausted minutes or change account billing. Measure actual PR duration after the new workflow runs before estimating savings.
