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

## Local Web UI

The bundled UI binds only to an ephemeral IPv4 loopback port. It does not inherit
ASP.NET/Kestrel endpoints from workspace configuration or environment variables.
Requests must use the published loopback Host; cross-origin browser requests are
rejected. Baseline changes require POST and a valid ASP.NET antiforgery token with
a SameSite cookie. Tokens use ephemeral protection keys, not persistent accounts.
Rendered evidence is HTML encoded. The UI disallows framing and inline scripts;
its small same-origin script only updates the analysis form submission state.

These safeguards protect the local browser surface, not against hostile processes
already running as the same user. Such processes can already access workspace
files and the CLI. Do not expose or reverse-proxy this server to other machines.
The UI never requests elevation, executes benchmark code from a web request, or
modifies archived evidence; selection uses the same event-first service as the CLI.

## AI and integrations

AI is optional and must not override measured evidence. Future tools, MCP servers, and observability integrations must use explicit capabilities, least-privilege credentials, and secret-safe logging. Model output must not become unrestricted shell execution.

## Managed execution

A future hosted or managed runner requires a stronger boundary than the local V1: container or VM isolation, resource limits, filesystem restrictions, network policy, tenant isolation, and dedicated secret handling.

## Reporting security issues

Email security reports to [Contact@alihaghighi.pro](mailto:Contact@alihaghighi.pro). Include the affected version, reproduction steps, and expected impact. Do not include live credentials or publish exploit details in a public issue.

The project is pre-release. Fixes target the current main branch; there is no supported long-term release line yet.
