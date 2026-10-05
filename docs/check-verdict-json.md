# Check verdict JSON contract

Status: V0.3 contract, schema `1.0`

Use:

```bash
perfagent check --candidate candidate.json --format json
```

The JSON written to stdout is the machine-readable deterministic verdict. It is not AI output.

## Exit codes

- `0`: PASS
- `1`: FAIL
- `2`: INCONCLUSIVE, or a command/input error

For an INCONCLUSIVE verdict, stdout contains a valid verdict document with
`"verdict": "inconclusive"`. Command/input errors are written to stderr and do not
produce a verdict document.

## Schema

Top-level fields:

- `schemaVersion`: contract version. Current value is `1.0`.
- `verdict`: `pass`, `fail`, or `inconclusive`.
- `candidate`: candidate evidence argument supplied to the command.
- `budget`: configured mean and allocation regression thresholds.
- `reasons`: aggregated deterministic reasons relevant to the verdict.
- `checks`: one or more baseline/reference checks.

A check contains:

- `reference.kind`: `explicit`, `runId`, `current`, or `anchor`.
- `reference.id`: source path or archived RunId when available.
- `verdict`
- `reasons`
- `benchmarks`

Each benchmark contains its verdict, reasons, and mean/allocation comparison:

- measured baseline value
- measured candidate value
- derived percentage change
- comparison status
- whether the configured budget was exceeded

Unavailable values remain explicit JSON `null`; they are never converted to zero.

## Example

```json
{
  "schemaVersion": "1.0",
  "verdict": "fail",
  "candidate": "candidate.json",
  "budget": {
    "maxMeanRegressionPercent": 5,
    "maxAllocationRegressionPercent": 10
  },
  "reasons": [
    "Sample.Work: mean regression exceeds the configured budget."
  ],
  "checks": [
    {
      "reference": {
        "kind": "explicit",
        "id": "baseline.json"
      },
      "verdict": "fail",
      "reasons": [
        "Sample.Work: mean regression exceeds the configured budget."
      ],
      "benchmarks": [
        {
          "name": "Sample.Work",
          "verdict": "fail",
          "reasons": [
            "Sample.Work: mean regression exceeds the configured budget."
          ],
          "mean": {
            "baseline": 100,
            "candidate": 110,
            "percentChange": 10,
            "status": "comparable",
            "budgetExceeded": true
          },
          "allocation": {
            "baseline": 64,
            "candidate": 64,
            "percentChange": 0,
            "status": "comparable",
            "budgetExceeded": false
          }
        }
      ]
    }
  ]
}
```

## Compatibility

Consumers should branch on `schemaVersion` before relying on fields. Additive fields
may be introduced within schema 1.x. Removing fields, renaming fields, or changing
their meaning requires a new schema version.
