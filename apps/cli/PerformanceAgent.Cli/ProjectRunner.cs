using PerformanceAgent.Core.Evidence;
using System.Diagnostics;

namespace PerformanceAgent.Cli;

internal sealed record ProjectRunResult(int ExitCode, string Evidence, string StandardOutput, string StandardError);
internal sealed record ProjectValidationResult(int ExitCode, string Validation, string StandardOutput, string StandardError);

internal sealed class ProjectRunner
{
    internal static readonly TimeSpan DefaultBenchmarkTimeout = TimeSpan.FromMinutes(30);

    public async Task<ProjectRunResult> RunAsync(
        string projectPath,
        CancellationToken cancellationToken = default,
        TimeSpan? benchmarkTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

            var hostAssembly = FindBenchmarkHostAssembly();
            var timeout = benchmarkTimeout ?? DefaultBenchmarkTimeout;
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(benchmarkTimeout), "Benchmark timeout must be greater than zero.");

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            ProcessResult host;
            try
            {
                host = await RunProcessAsync(
                    "dotnet",
                    [hostAssembly, assemblyPath, evidencePath],
                    timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Benchmark execution exceeded the timeout of {timeout}.");
            }

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


    public async Task<ProjectValidationResult> ValidateAsync(
        string projectPath,
        CancellationToken cancellationToken = default,
        TimeSpan? validationTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Benchmark project does not exist or is not a .csproj: {projectPath}");

        var targetPathResult = await RunProcessAsync(
            "dotnet",
            ["msbuild", fullPath, "-getProperty:TargetPath", "-property:Configuration=Release"],
            cancellationToken);

        if (targetPathResult.ExitCode != 0)
            return new ProjectValidationResult(
                targetPathResult.ExitCode,
                "",
                targetPathResult.StandardOutput,
                targetPathResult.StandardError);

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
            return new ProjectValidationResult(
                build.ExitCode,
                "",
                build.StandardOutput,
                build.StandardError);

        var timeout = validationTimeout ?? TimeSpan.FromMinutes(2);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(validationTimeout), "Validation timeout must be greater than zero.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        ProcessResult host;
        try
        {
            host = await RunProcessAsync(
                "dotnet",
                [FindBenchmarkHostAssembly(), "--validate", assemblyPath],
                timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Benchmark validation exceeded the timeout of {timeout}.");
        }

        return new ProjectValidationResult(
            host.ExitCode,
            host.StandardOutput,
            build.StandardOutput,
            build.StandardError + host.StandardError);
    }

    private static string FindBenchmarkHostAssembly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "benchmark-host", "PerformanceAgent.BenchmarkHost.dll");
        if (!File.Exists(path))
            throw new InvalidOperationException("The bundled Performance Agent benchmark host is missing. Reinstall or rebuild the tool.");

        return path;
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
        cancellationToken.ThrowIfCancellationRequested();
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch (OperationCanceledException) { /* Cancellation also interrupts pipe reads. */ }
            throw;
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void TryKillProcessTree(Process process)
    {
        if (process.HasExited)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // Process already exited and its handle is no longer available.
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
