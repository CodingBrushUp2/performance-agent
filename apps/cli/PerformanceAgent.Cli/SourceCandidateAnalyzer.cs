using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PerformanceAgent.Cli;

internal sealed class SourceCandidateAnalyzer
{
    public IReadOnlyList<SourceCandidate> Analyze(
        string repositoryRoot,
        IReadOnlyList<DiffFileChange> changes,
        int maxCandidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(changes);
        if (maxCandidates is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), "Candidate limit must be between 1 and 20.");

        var candidates = new List<SourceCandidate>();

        foreach (var change in changes.Where(change => IsEligiblePath(change.Path)))
        {
            var fullPath = Path.GetFullPath(change.Path, repositoryRoot);
            if (!File.Exists(fullPath))
                continue;

            var source = File.ReadAllText(fullPath);
            var tree = CSharpSyntaxTree.ParseText(source, path: fullPath);
            var root = tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var span = method.GetLocation().GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var endLine = span.EndLinePosition.Line + 1;
                var changedLineCount = change.ChangedRanges.Sum(range =>
                    CountOverlap(startLine, endLine, range.StartLine, range.EndLine));

                if (changedLineCount == 0)
                    continue;

                var typeName = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()?.Identifier.ValueText ?? "<top-level>";

                candidates.Add(new SourceCandidate(
                    change.Path,
                    typeName,
                    GetMemberName(method),
                    startLine,
                    endLine,
                    changedLineCount,
                    $"{changedLineCount} changed line(s) overlap this method in the current source."));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.ChangedLineCount)
            .ThenBy(candidate => candidate.FilePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.StartLine)
            .Take(maxCandidates)
            .ToArray();
    }

    public static bool IsEligiblePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                segment.Equals("test", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            return false;

        var fileName = Path.GetFileName(normalized);
        return fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        var start = Math.Max(firstStart, secondStart);
        var end = Math.Min(firstEnd, secondEnd);
        return end < start ? 0 : end - start + 1;
    }

    private static string GetMemberName(BaseMethodDeclarationSyntax method) =>
        method switch
        {
            MethodDeclarationSyntax value => value.Identifier.ValueText,
            ConstructorDeclarationSyntax value => value.Identifier.ValueText,
            DestructorDeclarationSyntax value => $"~{value.Identifier.ValueText}",
            OperatorDeclarationSyntax value => $"operator {value.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax value => $"operator {value.Type}",
            _ => method.Kind().ToString()
        };
}
