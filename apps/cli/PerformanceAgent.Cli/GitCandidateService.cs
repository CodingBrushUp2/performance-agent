using System.ComponentModel;
using System.Diagnostics;

namespace PerformanceAgent.Cli;

internal sealed class GitCandidateService
{
    public async Task<CandidateAnalysisResult> AnalyzeAsync(
        string baseRef,
        string headRef,
        int maxCandidates,
        CancellationToken cancellationToken = default)
    {
        ValidateRef(baseRef, nameof(baseRef));
        ValidateRef(headRef, nameof(headRef));

        var rootResult = await RunGitAsync(
            Environment.CurrentDirectory,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);

        if (rootResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Current directory is not a readable Git repository: {rootResult.StandardError.Trim()}");

        var repositoryRoot = rootResult.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            throw new InvalidOperationException("Git did not return a valid repository root.");

        var diffResult = await RunGitAsync(
            repositoryRoot,
            [
                "-c", "core.quotePath=false",
                "diff",
                "--unified=0",
                "--no-color",
                "--no-ext-diff",
                "--no-renames",
                $"{baseRef}...{headRef}",
                "--",
                ":(glob)**/*.cs"
            ],
            cancellationToken);

        if (diffResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Git diff failed for '{baseRef}...{headRef}': {diffResult.StandardError.Trim()}");

        var changes = UnifiedDiffParser.Parse(diffResult.StandardOutput);
        var eligibleChanges = changes
            .Where(change => SourceCandidateAnalyzer.IsEligiblePath(change.Path))
            .Where(change => File.Exists(Path.GetFullPath(change.Path, repositoryRoot)))
            .ToArray();

        var candidates = new SourceCandidateAnalyzer().Analyze(
            repositoryRoot,
            changes,
            maxCandidates);

        return new CandidateAnalysisResult(
            baseRef,
            headRef,
            changes.Count,
            eligibleChanges.Length,
            candidates);
    }

    private static void ValidateRef(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.StartsWith("-", StringComparison.Ordinal))
            throw new ArgumentException("Git refs beginning with '-' are not accepted.", parameterName);
    }

    private static async Task<GitProcessResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Git process could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Git executable was not found. Install Git and ensure it is available on PATH.",
                exception);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

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
                // Process already exited between the checks.
            }

            throw;
        }

        return new GitProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private sealed record GitProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
