using PerformanceAgent.Core.Evidence;
using System.Diagnostics;

namespace PerformanceAgent.Cli;

internal sealed record ProjectRunResult(int ExitCode, string Evidence, string StandardOutput, string StandardError);

internal sealed class ProjectRunner
{
    public async Task<ProjectRunResult> RunAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Benchmark project does not exist or is not a .csproj: {projectPath}");

        var evidencePath = Path.Combine(Path.GetTempPath(), $"performance-agent-{Guid.NewGuid():N}.json");
        try
        {
            var targetPathResult = await RunProcessAsync(
                "dotnet",
                ["msbuild", fullPath, "-getProperty:TargetPath", "-property:Configuration=Release"],
                cancellationToken);

            if (targetPathResult.ExitCode != 0)
                return new ProjectRunResult(targetPathResult.ExitCode, "", targetPathResult.StandardOutput, targetPathResult.StandardError);

            var assemblyPath = targetPathResult.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Could not determine the benchmark project's target assembly.");

            if (!Path.IsPathRooted(assemblyPath))
                assemblyPath = Path.GetFullPath(assemblyPath, Path.GetDirectoryName(fullPath)!);

            var build = await RunProcessAsync(
                "dotnet",
                ["build", fullPath, "--configuration", "Release"],
                cancellationToken);

            if (build.ExitCode != 0)
                return new ProjectRunResult(build.ExitCode, "", build.StandardOutput, build.StandardError);

            var hostProject = FindBenchmarkHostProject();
            var host = await RunProcessAsync(
                "dotnet",
                ["run", "--project", hostProject, "--configuration", "Release", "--no-build", "--", assemblyPath, evidencePath],
                cancellationToken);

            var evidence = File.Exists(evidencePath)
                ? await File.ReadAllTextAsync(evidencePath, cancellationToken)
                : string.Empty;

            if (host.ExitCode == 0)
            {
                if (string.IsNullOrWhiteSpace(evidence))
                    throw new InvalidOperationException("Benchmark host completed successfully but did not produce Performance Agent evidence.");

                _ = new JsonBenchmarkEvidenceReader().Read(evidence);
            }

            return new ProjectRunResult(host.ExitCode, evidence, build.StandardOutput + host.StandardOutput, build.StandardError + host.StandardError);
        }
        finally
        {
            if (File.Exists(evidencePath))
                File.Delete(evidencePath);
        }
    }

    private static string FindBenchmarkHostProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "apps", "benchmark-host", "PerformanceAgent.BenchmarkHost", "PerformanceAgent.BenchmarkHost.csproj");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Performance Agent benchmark host could not be located.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
