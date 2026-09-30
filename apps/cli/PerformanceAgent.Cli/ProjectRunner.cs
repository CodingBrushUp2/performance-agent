using System.Diagnostics;

namespace PerformanceAgent.Cli;

internal sealed record ProjectRunResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class ProjectRunner
{
    public async Task<ProjectRunResult> RunAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);

        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Benchmark project does not exist or is not a .csproj: {projectPath}");
        }

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

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProjectRunResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}
