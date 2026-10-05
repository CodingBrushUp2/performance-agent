using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PerformanceAgent.Cli;

internal sealed record CandidateHint(
    string FilePath,
    string Member,
    string Kind,
    int StartLine,
    int ChangedLines,
    string Reason);

internal sealed record CandidateDiscoveryResult(
    string SchemaVersion,
    string BaseRef,
    string HeadRef,
    int Limit,
    IReadOnlyList<CandidateHint> Candidates);

internal sealed class GitDiffCandidateService
{
    private static readonly Regex HunkHeader = new(
        @"^@@\s+-\d+(?:,\d+)?\s+\+(?<start>\d+)(?:,(?<count>\d+))?\s+@@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<CandidateDiscoveryResult> DiscoverAsync(
        string baseRef,
        string headRef = "HEAD",
        int limit = 5,
        CancellationToken cancellationToken = default,
        string? repositoryPath = null,
        bool workingTree = false)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
            throw new ArgumentException("A non-empty git base ref is required.", nameof(baseRef));
        if (!workingTree && string.IsNullOrWhiteSpace(headRef))
            throw new ArgumentException("A non-empty git head ref is required.", nameof(headRef));
        if (limit is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(limit), "Candidate limit must be between 1 and 20.");

        var rootResult = await RunGitAsync(
            repositoryPath is null ? Environment.CurrentDirectory : Path.GetFullPath(repositoryPath),
            ["rev-parse", "--show-toplevel"],
            cancellationToken);
        if (rootResult.ExitCode != 0)
            throw GitFailure("Cannot resolve the git repository root.", rootResult);

        var repositoryRoot = rootResult.StandardOutput.Trim();
        if (repositoryRoot.Length == 0)
            throw new InvalidOperationException("Git returned an empty repository root.");

        var diffArguments = new List<string>
        {
            "-c", "core.quotepath=false",
            "diff",
            "--unified=0",
            "--no-color",
            "--find-renames"
        };
        diffArguments.Add(workingTree ? baseRef : $"{baseRef}...{headRef}");
        diffArguments.Add("--");
        diffArguments.Add(":(glob)**/*.cs");

        var diffResult = await RunGitAsync(
            repositoryRoot,
            diffArguments,
            cancellationToken);
        if (diffResult.ExitCode != 0)
            throw GitFailure(
                workingTree
                    ? $"Cannot read git diff for '{baseRef}..WORKTREE'."
                    : $"Cannot read git diff for '{baseRef}...{headRef}'.",
                diffResult);

        var changedFiles = ParseUnifiedDiff(diffResult.StandardOutput);
        var candidates = new List<CandidateHint>();

        foreach (var file in changedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(
                file.Path.Replace('/', Path.DirectorySeparatorChar),
                repositoryRoot);

            if (!File.Exists(fullPath))
                continue;

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            candidates.AddRange(MapCandidates(file.Path, source, file.ChangedLines));
        }

        var ranked = candidates
            .GroupBy(
                item => (item.FilePath, item.Member, item.Kind, item.StartLine),
                item => item,
                EqualityComparer<(string, string, string, int)>.Default)
            .Select(group =>
            {
                var sample = group.First();
                var changedLines = group.Sum(item => item.ChangedLines);
                return sample with
                {
                    ChangedLines = changedLines,
                    Reason = $"{changedLines} changed line(s) overlap this member in the selected git diff."
                };
            })
            .OrderByDescending(item => item.ChangedLines)
            .ThenBy(item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.StartLine)
            .ThenBy(item => item.Member, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return new CandidateDiscoveryResult(
            "1.0",
            baseRef,
            workingTree ? "WORKTREE" : headRef,
            limit,
            ranked);
    }

    internal static IReadOnlyList<ChangedFile> ParseUnifiedDiff(string diff)
    {
        var files = new List<ChangedFile>();
        string? currentPath = null;
        List<int>? changedLines = null;

        foreach (var rawLine in diff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.StartsWith("+++ ", StringComparison.Ordinal))
            {
                Flush();
                var value = rawLine[4..];
                if (string.Equals(value, "/dev/null", StringComparison.Ordinal))
                    continue;

                currentPath = value.StartsWith("b/", StringComparison.Ordinal)
                    ? value[2..]
                    : value;
                changedLines = [];
                continue;
            }

            if (currentPath is null || changedLines is null)
                continue;

            var match = HunkHeader.Match(rawLine);
            if (!match.Success)
                continue;

            var start = int.Parse(match.Groups["start"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var countGroup = match.Groups["count"];
            var count = countGroup.Success
                ? int.Parse(countGroup.Value, System.Globalization.CultureInfo.InvariantCulture)
                : 1;

            for (var line = start; line < start + count; line++)
            {
                if (line > 0)
                    changedLines.Add(line);
            }
        }

        Flush();
        return files;

        void Flush()
        {
            if (currentPath is not null && changedLines is not null && changedLines.Count != 0)
            {
                files.Add(new ChangedFile(
                    currentPath,
                    changedLines.Distinct().Order().ToArray()));
            }

            currentPath = null;
            changedLines = null;
        }
    }

    internal static IReadOnlyList<CandidateHint> MapCandidates(
        string filePath,
        string source,
        IReadOnlyList<int> changedLines)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetRoot();
        var text = tree.GetText();

        var counts = new Dictionary<SyntaxNode, int>(ReferenceEqualityComparer.Instance);

        foreach (var lineNumber in changedLines.Distinct())
        {
            if (lineNumber < 1 || lineNumber > text.Lines.Count)
                continue;

            var line = text.Lines[lineNumber - 1];
            var position = FirstContentPosition(source, line.Start, line.End);
            var token = root.FindToken(position, findInsideTrivia: true);
            var member = token.Parent?.AncestorsAndSelf().FirstOrDefault(IsCandidateMember);
            if (member is null)
                continue;

            counts[member] = counts.GetValueOrDefault(member) + 1;
        }

        return counts.Select(pair =>
        {
            var span = pair.Key.GetLocation().GetLineSpan();
            return new CandidateHint(
                filePath,
                FormatMember(pair.Key),
                Kind(pair.Key),
                span.StartLinePosition.Line + 1,
                pair.Value,
                $"{pair.Value} changed line(s) overlap this member in the selected git diff.");
        }).ToArray();
    }

    private static int FirstContentPosition(string source, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(source[index]))
                return index;
        }

        return start;
    }

    private static bool IsCandidateMember(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax
            or LocalFunctionStatementSyntax
            or AccessorDeclarationSyntax;

    private static string Kind(SyntaxNode node) => node switch
    {
        ConstructorDeclarationSyntax => "constructor",
        DestructorDeclarationSyntax => "destructor",
        OperatorDeclarationSyntax => "operator",
        ConversionOperatorDeclarationSyntax => "conversion-operator",
        LocalFunctionStatementSyntax => "local-function",
        AccessorDeclarationSyntax => "accessor",
        MethodDeclarationSyntax => "method",
        _ => "member"
    };

    private static string FormatMember(SyntaxNode node)
    {
        var typePrefix = string.Join(
            ".",
            node.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .Reverse()
                .Select(type => type.Identifier.ValueText));

        var member = node switch
        {
            MethodDeclarationSyntax method =>
                $"{method.Identifier.ValueText}({FormatParameters(method.ParameterList)})",
            ConstructorDeclarationSyntax constructor =>
                $"{constructor.Identifier.ValueText}({FormatParameters(constructor.ParameterList)})",
            DestructorDeclarationSyntax destructor =>
                $"~{destructor.Identifier.ValueText}()",
            OperatorDeclarationSyntax op =>
                $"operator {op.OperatorToken.ValueText}({FormatParameters(op.ParameterList)})",
            ConversionOperatorDeclarationSyntax conversion =>
                $"{conversion.ImplicitOrExplicitKeyword.ValueText} operator {conversion.Type}({FormatParameters(conversion.ParameterList)})",
            LocalFunctionStatementSyntax local =>
                $"{local.Identifier.ValueText}({FormatParameters(local.ParameterList)})",
            AccessorDeclarationSyntax accessor =>
                $"{AccessorOwner(accessor)}.{accessor.Keyword.ValueText}",
            _ => node.Kind().ToString()
        };

        return string.IsNullOrEmpty(typePrefix) ? member : $"{typePrefix}.{member}";
    }

    private static string AccessorOwner(AccessorDeclarationSyntax accessor) =>
        accessor.Parent?.Parent switch
        {
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax => "this[]",
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
            _ => "accessor"
        };

    private static string FormatParameters(ParameterListSyntax parameters) =>
        string.Join(", ", parameters.Parameters.Select(parameter =>
            parameter.Type?.ToString() ?? "?"));

    private static async Task<GitProcessResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Git is required for candidate discovery and could not be started.",
                exception);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        return new GitProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static InvalidOperationException GitFailure(
        string message,
        GitProcessResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return new InvalidOperationException(
            details.Length == 0 ? message : $"{message} {details}");
    }

    internal sealed record ChangedFile(string Path, IReadOnlyList<int> ChangedLines);
    private sealed record GitProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
