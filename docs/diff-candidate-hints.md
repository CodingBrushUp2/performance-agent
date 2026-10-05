# Diff-based benchmark candidate hints

V0.5 starts with a deterministic source-selection helper:

```bash
perfagent candidates --base origin/main
perfagent candidates --base HEAD --working-tree
perfagent candidates --base main --head HEAD --limit 5 --format json
```

The command does not generate benchmarks and does not claim that a changed method must
be benchmarked.

It performs three narrow steps:

1. ask Git for the selected `base...head` C# diff;
2. map changed target-line numbers to the current C# syntax tree with Roslyn;
3. rank touched members by the number of changed lines and return at most the requested
   limit.

The default head is `HEAD`. Use `--working-tree` to compare the base directly with the current working tree, including committed, staged, and unstaged changes since that base. `--working-tree` and `--head` are mutually exclusive. The default limit is 5 and the accepted range is 1–20.

## Output

Each candidate contains:

- repository-relative file path;
- C# member name;
- member kind;
- source start line;
- number of changed lines mapped to the member;
- a deterministic reason.

JSON output uses schema `1.0`.

## Noise filtering

Candidate discovery ignores obvious non-production noise:

- `test` / `tests` path segments;
- `bin` and `obj`;
- `*Test.cs` and `*Tests.cs`;
- common generated names such as `.g.cs`, `.g.i.cs`, and `.Designer.cs`.

Git refs beginning with `-` are rejected so a ref cannot be interpreted as an
additional Git command-line option.

## Boundaries

Candidate hints are deliberately not benchmark recommendations.

This slice does not:

- infer production hot paths;
- use runtime profiles;
- calculate complexity scores;
- call an LLM;
- generate BenchmarkDotNet wrappers;
- inspect every assembly member;
- add an assembly browser or UI.

A developer or coding agent still decides whether the changed member is performance
relevant and whether an existing benchmark covers it.

Later V0.5 slices may add narrow, evidence-backed ranking signals, but only after this
diff-to-member mapping proves useful.
