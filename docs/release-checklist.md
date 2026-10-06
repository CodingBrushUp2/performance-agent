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

- [x] Validate the implemented source and locally packaged tool end-to-end. The
  source tested was main `6b0e1bbc39150ddfbf1f4388682e65e7b6bb1d86`.
  Repeat affected checks if executable code, packaging, or dependencies change;
  build any public binary release from its intended release tag.
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

## Full local validation follow-up

Completed on 2026-10-06 against the implemented source from main
`6b0e1bbc39150ddfbf1f4388682e65e7b6bb1d86`, using .NET SDK 10.0.401.

| Check | Result |
| --- | --- |
| Release solution build | Passed, zero warnings/errors |
| Unit tests | 221 passed (Core 161, AI 50, BenchmarkDotNet 10) |
| CLI integration tests | 103 passed |
| BenchmarkDotNet integration tests | All 11 distinct tests passed; see environment note below |
| CLI, trusted-verdict, history, discovery, cancellation, and UI smoke scripts | All six passed |
| Installed-tool end-to-end script | Passed with an isolated tool manifest and package cache outside the source tree |
| Fresh E2E package license/readme/notice contents | Verified against source files |

The installed-tool test executed a real benchmark, checked its confidence statistics
and immutable archive, ran two calibration measurements without changing baselines,
exercised Git candidate/readiness output, changed Current/Anchor through the local UI,
exported HTML, checked optional AI failure without contacting OpenAI, and verified the
error for a damaged installation. Its deliberately loose calibration spread threshold
is a smoke-test setting, not evidence of production measurement stability.

### Environment and timing notes

The first default BenchmarkDotNet integration run passed nine tests and failed two
execution tests because this host denies Unix sockets used by MSBuild worker nodes.
Both unchanged execution tests then passed with the standard MSBuild property
`BuildInParallel=false`, which prevents parallel project-reference builds. Local
restore/build/pack commands also used a single MSBuild node and disabled node reuse.
No product code, assertion, or measurement-quality policy was changed.

On a similarly restricted Unix host, set the property for the benchmark tests and
end-to-end scripts:

```bash
export BuildInParallel=false
```

The first CLI smoke attempt exceeded a three-minute wrapper allowance. The unchanged
script passed with a seven-minute allowance in 263.4 seconds; its two default benchmark
runs adapt to measurement noise. The installed-tool script passed in 178.9 seconds.
These durations describe this host, not a runtime or performance guarantee.

No hosted Actions run or paid AI analysis was requested for this validation. The
repository remains private; no public package, release tag, or visibility change was made.

External adoption and commercial demand have not been validated. A benchmark PASS
is not proof that all changed code is covered, or that production performance is
unchanged. Public source availability does not imply production certification.
