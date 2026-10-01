namespace PerformanceAgent.Cli;

internal sealed record WorkspaceStorageStatus(string WorkspaceDirectory, string StateDirectory, bool Writable, string? Error);

internal sealed record WorkspaceStorage(string WorkspaceDirectory, string StateDirectory)
{
    public WorkspaceStorageStatus Inspect()
    {
        try
        {
            EnsureWritable();
            return new(WorkspaceDirectory, StateDirectory, true, null);
        }
        catch (InvalidOperationException exception)
        {
            return new(WorkspaceDirectory, StateDirectory, false, exception.Message);
        }
    }

    public static WorkspaceStorage Resolve(string? workspaceDirectory = null)
    {
        var workspace = Path.GetFullPath(workspaceDirectory ?? Environment.CurrentDirectory);
        return new WorkspaceStorage(workspace, Path.Combine(workspace, ".performance-agent"));
    }

    public void EnsureWritable()
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            var probe = Path.Combine(StateDirectory, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Performance Agent cannot write to its state directory '{StateDirectory}'. Fix the folder permissions or use a writable workspace. Administrator/root privileges are not required or recommended.",
                exception);
        }
    }
}
