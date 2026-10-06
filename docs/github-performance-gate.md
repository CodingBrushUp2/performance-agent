# GitHub performance regression gate

Performance Agent can act as a deterministic CI gate without an LLM.

The example workflow compares a version-controlled candidate evidence file with a version-controlled baseline and applies the repository's performance budget.

```text
baseline.json + candidate.json + performance-budget.json
                         |
                         v
                   perfagent check
                         |
                    PASS / FAIL
                         |
                  CI exit code 0 / 1
```

The repository workflow is manual only: select **Actions > Performance Gate Demo > Run workflow**. Routine PR CI and manual full validation are described in [CI execution policy](ci-policy.md).

The checked-in fixtures under `samples/ci` are intentionally deterministic so the repository CI tests the gate itself without benchmark noise.

For a real project, generate benchmark evidence on a controlled runner, preserve the accepted result as the baseline, generate candidate evidence for the proposed change, and run the same `perfagent check` command. Keep runtime, OS, and architecture aligned; Performance Agent rejects incomparable evidence.

This demo does not claim that GitHub-hosted runners provide stable enough hardware for authoritative microbenchmark baselines. Teams that enforce tight performance budgets should use controlled or self-hosted benchmark runners.
