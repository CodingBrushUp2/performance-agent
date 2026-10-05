using PerformanceAgent.Cli;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class DiffCandidateTests
{
    [Fact]
    public void UnifiedDiffParser_ExtractsCurrentSideRanges()
    {
        const string diff = """
        diff --git a/src/Worker.cs b/src/Worker.cs
        --- a/src/Worker.cs
        +++ b/src/Worker.cs
        @@ -4,2 +4,3 @@
        @@ -20 +21,2 @@
        """;

        var change = Assert.Single(UnifiedDiffParser.Parse(diff));

        Assert.Equal("src/Worker.cs", change.Path);
        Assert.Collection(
            change.ChangedRanges,
            range =>
            {
                Assert.Equal(4, range.StartLine);
                Assert.Equal(3, range.LineCount);
            },
            range =>
            {
                Assert.Equal(21, range.StartLine);
                Assert.Equal(2, range.LineCount);
            });
    }

    [Fact]
    public void Analyzer_ReturnsChangedProductionMethodAndSkipsTests()
    {
        var root = CreateDirectory();
        try
        {
            Write(root, "src/Worker.cs", """
                namespace Demo;

                public class Worker
                {
                    public int First(int value)
                    {
                        return value + 1;
                    }

                    public int Second(int value)
                    {
                        var doubled = value * 2;
                        return doubled;
                    }
                }
                """);

            Write(root, "tests/WorkerTests.cs", """
                public class WorkerTests
                {
                    public void ChangedTest()
                    {
                        _ = 42;
                    }
                }
                """);

            var changes = new[]
            {
                new DiffFileChange("src/Worker.cs", [new ChangedLineRange(12, 2)]),
                new DiffFileChange("tests/WorkerTests.cs", [new ChangedLineRange(3, 3)])
            };

            var candidate = Assert.Single(new SourceCandidateAnalyzer().Analyze(root, changes, 5));

            Assert.Equal("src/Worker.cs", candidate.FilePath);
            Assert.Equal("Worker", candidate.TypeName);
            Assert.Equal("Second", candidate.MemberName);
            Assert.True(candidate.ChangedLineCount > 0);
            Assert.Contains("changed line", candidate.Reason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyzer_RanksByChangedLineOverlapAndHonorsLimit()
    {
        var root = CreateDirectory();
        try
        {
            Write(root, "src/Worker.cs", """
                public class Worker
                {
                    public int Small(int value)
                    {
                        return value + 1;
                    }

                    public int Large(int value)
                    {
                        var first = value + 1;
                        var second = first + 1;
                        var third = second + 1;
                        return third;
                    }
                }
                """);

            var changes = new[]
            {
                new DiffFileChange(
                    "src/Worker.cs",
                    [
                        new ChangedLineRange(5, 1),
                        new ChangedLineRange(10, 4)
                    ])
            };

            var candidate = Assert.Single(new SourceCandidateAnalyzer().Analyze(root, changes, 1));

            Assert.Equal("Large", candidate.MemberName);
            Assert.True(candidate.ChangedLineCount >= 3);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("tests/Worker.cs")]
    [InlineData("src/WorkerTests.cs")]
    [InlineData("src/Generated.g.cs")]
    [InlineData("src/View.Designer.cs")]
    [InlineData("obj/Release/Generated.cs")]
    public void EligibilityFilter_ExcludesNoise(string path)
    {
        Assert.False(SourceCandidateAnalyzer.IsEligiblePath(path));
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "performance-agent-candidates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
