using System.Diagnostics;
using PerformanceAgent.Cli;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class GitDiffCandidateServiceTests
{
    [Fact]
    public void ParseUnifiedDiff_CollectsOnlyAddedOrModifiedTargetLines()
    {
        const string diff = """
        diff --git a/src/Sample.cs b/src/Sample.cs
        index 1111111..2222222 100644
        --- a/src/Sample.cs
        +++ b/src/Sample.cs
        @@ -2,0 +3,2 @@
        +line 3
        +line 4
        @@ -10,2 +11,0 @@
        -deleted
        -deleted
        """;

        var file = Assert.Single(GitDiffCandidateService.ParseUnifiedDiff(diff));

        Assert.Equal("src/Sample.cs", file.Path);
        Assert.Equal(new[] { 3, 4 }, file.ChangedLines);
    }

    [Fact]
    public void MapCandidates_MapsChangedLinesToInnermostMembers()
    {
        const string source = """
        public class Sample
        {
            public int First()
            {
                var value = 1;
                return value;
            }

            public int Second()
            {
                return 2;
            }
        }
        """;

        var candidates = GitDiffCandidateService.MapCandidates(
            "Sample.cs",
            source,
            [5, 6, 11]);

        Assert.Equal(2, candidates.Count);

        var first = Assert.Single(candidates, item => item.Member == "Sample.First()");
        Assert.Equal("method", first.Kind);
        Assert.Equal(2, first.ChangedLines);

        var second = Assert.Single(candidates, item => item.Member == "Sample.Second()");
        Assert.Equal(1, second.ChangedLines);
    }

    [Theory]
    [InlineData("src/OrderService.cs", true)]
    [InlineData("tests/OrderServiceTests.cs", false)]
    [InlineData("test/OrderService.cs", false)]
    [InlineData("src/Generated.g.cs", false)]
    [InlineData("src/Generated.g.i.cs", false)]
    [InlineData("src/Form.Designer.cs", false)]
    [InlineData("src/obj/Generated.cs", false)]
    [InlineData("src/bin/Generated.cs", false)]
    [InlineData("src/OrderServiceTest.cs", false)]
    public void IsEligiblePath_FiltersObviousNonProductionNoise(string path, bool expected)
    {
        Assert.Equal(expected, GitDiffCandidateService.IsEligiblePath(path));
    }

    [Fact]
    public async Task DiscoverAsync_RejectsGitRefsThatLookLikeOptions()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            new GitDiffCandidateService().DiscoverAsync("--help"));

        Assert.Contains("beginning with '-'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_UsesRealGitDiffAndRanksTouchedMembers()
    {
        using var workspace = new TemporaryDirectory();
        RunGit(workspace.Path, "init");
        RunGit(workspace.Path, "config", "user.email", "perfagent@example.invalid");
        RunGit(workspace.Path, "config", "user.name", "Performance Agent Tests");

        var sourcePath = Path.Combine(workspace.Path, "Sample.cs");
        await File.WriteAllTextAsync(sourcePath, """
        public class Sample
        {
            public int First()
            {
                return 1;
            }

            public int Second()
            {
                return 2;
            }
        }
        """);
        RunGit(workspace.Path, "add", "Sample.cs");
        RunGit(workspace.Path, "commit", "-m", "baseline");
        var baseRef = RunGit(workspace.Path, "rev-parse", "HEAD").Trim();

        await File.WriteAllTextAsync(sourcePath, """
        public class Sample
        {
            public int First()
            {
                var value = 10;
                value += 5;
                return value;
            }

            public int Second()
            {
                return 3;
            }
        }
        """);
        RunGit(workspace.Path, "add", "Sample.cs");
        RunGit(workspace.Path, "commit", "-m", "candidate");

        var result = await new GitDiffCandidateService().DiscoverAsync(
            baseRef,
            "HEAD",
            limit: 5,
            repositoryPath: workspace.Path);

        Assert.Equal("1.0", result.SchemaVersion);
        Assert.Equal(baseRef, result.BaseRef);
        Assert.Equal("HEAD", result.HeadRef);
        Assert.Equal(2, result.Candidates.Count);

        Assert.Equal("Sample.First()", result.Candidates[0].Member);
        Assert.True(result.Candidates[0].ChangedLines > result.Candidates[1].ChangedLines);
        Assert.Equal("Sample.Second()", result.Candidates[1].Member);
        Assert.All(result.Candidates, item =>
            Assert.Contains("changed line(s)", item.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsync_WorkingTreeIncludesUncommittedChanges()
    {
        using var workspace = new TemporaryDirectory();
        RunGit(workspace.Path, "init");
        RunGit(workspace.Path, "config", "user.email", "perfagent@example.invalid");
        RunGit(workspace.Path, "config", "user.name", "Performance Agent Tests");

        var sourcePath = Path.Combine(workspace.Path, "Sample.cs");
        await File.WriteAllTextAsync(sourcePath, """
        public class Sample
        {
            public int Work()
            {
                return 1;
            }
        }
        """);
        RunGit(workspace.Path, "add", "Sample.cs");
        RunGit(workspace.Path, "commit", "-m", "baseline");
        var baseRef = RunGit(workspace.Path, "rev-parse", "HEAD").Trim();

        await File.WriteAllTextAsync(sourcePath, """
        public class Sample
        {
            public int Work()
            {
                var value = 2;
                return value;
            }
        }
        """);

        var result = await new GitDiffCandidateService().DiscoverAsync(
            baseRef,
            limit: 5,
            repositoryPath: workspace.Path,
            workingTree: true);

        Assert.Equal("WORKTREE", result.HeadRef);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Sample.Work()", candidate.Member);
        Assert.True(candidate.ChangedLines >= 1);
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");

        return stdout;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"performance-agent-candidates-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
