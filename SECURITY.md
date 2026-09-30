# Security model

Performance Agent executes benchmark projects supplied by the user. Benchmark code is executable code, not passive input.

## Trust boundary

Run `perfagent` only against projects and benchmark assemblies you trust. Process isolation is used for reliability and cleanup, but the benchmark host is **not a security sandbox**. Benchmark code runs with the permissions of the invoking user and may access files, network resources, environment variables, or start child processes.

Do not run untrusted pull-request code on runners that contain secrets or have access to sensitive networks or files.

## Current safeguards

- Benchmark execution is isolated from the CLI process.
- Benchmark-host output is validated before it is accepted as Performance Agent evidence.
- Benchmark execution has a bounded default timeout.
- Cancellation terminates the benchmark process tree.
- Temporary evidence files use randomized names and are deleted after execution.
- Secrets must not be stored in evidence, repository configuration, prompts, or logs.

## AI and integrations

AI is optional and must not override measured evidence. Future tools, MCP servers, and observability integrations must use explicit capabilities, least-privilege credentials, and secret-safe logging. Model output must not become unrestricted shell execution.

## Managed execution

A future hosted or managed runner requires a stronger boundary than the local V1: container or VM isolation, resource limits, filesystem restrictions, network policy, tenant isolation, and dedicated secret handling.

## Reporting security issues

This project is currently private and pre-release. Report security issues privately to the repository owner rather than opening a public issue. A public vulnerability-reporting process will be added before public release.
