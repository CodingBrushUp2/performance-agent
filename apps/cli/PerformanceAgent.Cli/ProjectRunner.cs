using System.Diagnostics;

namespace PerformanceAgent.Cli;

internal sealed record ProjectRunResult(int ExitCode, string Evidence, string StandardOutput, string StandardError);

internal sealed class ProjectRunner
{
    public async Task<ProjectRunResult> RunAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);

        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Benchmark project does not exist or is not a .csproj: {projectPath}");
        }

        var evidencePath = Path.Combine(Path.GetTempPath(), $"performance-agent-{Guid.NewGuid():N}.json");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(fullPath);
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--performance-agent-output");
            startInfo.ArgumentList.Add(evidencePath);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var evidence = File.Exists(evidencePath)
                ? await File.ReadAllTextAsync(evidencePath, cancellationToken)
                : string.Empty;

            if (process.ExitCode == 0 && string.IsNullOrWhiteSpace(evidence))
            {
                throw new InvalidOperationException(
                    "Benchmark project completed successfully but did not produce Performance Agent evidence.");
            }

            return new ProjectRunResult(process.ExitCode, evidence, stdout, stderr);
        }
        finally
        {
            if (File.Exists(evidencePath))
            {
                File.Delete(evidencePath);
            }
        }
    }
}
