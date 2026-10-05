# Diff-based method candidates

V0.5 starts with a narrow changed-code discovery command:

```bash
perfagent candidates <base-ref>
perfagent candidates <base-ref> --head <ref> --max 5
perfagent candidates <base-ref> --format json
```

The command asks Git for the committed C# diff between `<base-ref>...<head-ref>`
(`HEAD` by default), maps current-side changed line ranges to C# methods with Roslyn,
and returns at most five changed methods by default.

## What a candidate means

A candidate is a **focus hint**, not a performance-risk verdict.

The first V0.5 slice ranks methods only by how many changed lines from the requested
diff overlap the current method declaration/body. It does not claim that a method is
slow, hot, allocation-heavy, or worth benchmarking merely because it changed.

Each result includes:

- repository-relative source file;
- containing type;
- member name;
- current source line range;
- count of changed lines overlapping that method;
- a plain-language reason.

JSON output uses schema `1.0`.

## Noise controls

The first slice excludes:

- paths under `test` / `tests`;
- `bin` and `obj`;
- `*Test.cs` and `*Tests.cs`;
- generated `.g.cs`, `.g.i.cs`, and `.Designer.cs` files.

This is intentionally not an assembly browser and it does not enumerate every method.

## Current limitations

- only committed Git diffs are considered;
- uncommitted working-tree changes are not included yet;
- deleted-only hunks cannot be mapped to a method in the current source and are skipped;
- no semantic model, runtime profile, AI ranking, or benchmark generation is used;
- no candidate is automatically benchmarked.

Later V0.5 slices may add conservative performance-relevance signals or profile evidence
only if they improve precision without turning the command into a noisy static-analysis
browser.
