# Project closeout and publication checklist

Decision date: 2026-10-06.

The implemented scope is closed to new feature work. The project is retained for
learning, demonstrations, and portfolio use. Small bug/security fixes and dependency
maintenance may be considered; no response-time or release commitment is made.
The repository is not being archived, because archiving would disable normal
maintenance. Historical specifications describe the design and are not delivery
commitments. The implemented-scope checklist in [README](../README.md) is authoritative
for current capability claims.

## Prepared for publication

- [x] Document shipped capabilities and known limitations.
- [x] Separate deferred ideas from active commitments.
- [x] Add the project MIT license and align CLI package license metadata.
- [x] Inventory resolved CLI and BenchmarkHost runtime dependencies and preserve
  upstream licenses/notices, including Capstone native and LLVM notices.
- [x] Include the license, README, and third-party notice bundle in local packaging.
- [x] Document local installation and test its basic installed-tool path.
- [x] Provide contribution and private security-reporting instructions.
- [x] Record the scoped source/history/dependency review and its exclusions.

## Before a public binary release

- [ ] Complete full end-to-end validation of the exact release commit on a host
  where benchmark subprocess execution is supported. Two prior execution tests
  were blocked by this environment's MSBuild Unix-pipe permissions; do not hide
  those failures or describe the full suite as green.
- [ ] Verify the intended public package name/version and inspect the final NuGet
  artifact. Version 0.5.0 is currently a local release candidate, not a published
  NuGet package. Do not overwrite a previously published version.
- [ ] Refresh the transitive vulnerability audit and regenerate/check notices if
  any resolved dependency changes. A past clean audit is not a future guarantee.
- [ ] Review public repository settings and contribution permissions. Restricted
  maintenance is the intended scope; no hosted CI or AI spend is required to
  demonstrate the deterministic fixture workflow.
- [ ] Change repository visibility when the owner decides to publish the source.
- [ ] Create the release tag and release notes after validation, then decide
  whether to publish a NuGet package. No tag or package is created by this closeout.

## Portfolio follow-up (documentation only)

- [ ] Publish a project page with architecture, sample evidence, and limitations.
- [ ] Record a short baseline/regression/INCONCLUSIVE demonstration.
- [ ] Write articles on measurement noise, conservative performance gates, and
  separating AI advice from deterministic decisions.

## Closeout verification

- CLI restore and Release pack succeeded without build warnings or errors.
- The NuGet artifact has MIT license metadata, the README, BenchmarkHost, and the
  complete legal inventory at the package root and alongside the tool binaries.
- All notice hashes and the 31-package inventory match the restored runtime graphs.
- A fresh local tool installation outside the source checkout passed general help,
  check help, and a deterministic Markdown comparison. Installed notices were checked.
- CLI and BenchmarkHost transitive vulnerability audits reported no known vulnerable
  packages on 2026-10-06.
- Documentation links and whitespace were checked. No C# implementation changed.

These checks cover documentation and packaging. They do not replace the pending
full benchmark-execution validation for a public binary release. Hosted CI and
external AI analysis are not needed for these closeout checks.

External adoption and commercial demand have not been validated. A benchmark PASS
is not proof that all changed code is covered, or that production performance is
unchanged. Public source availability does not imply production certification.
