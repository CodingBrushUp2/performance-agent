using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class ProjectRunnerValidationTests
{
    [Fact]
    public async Task BuildFailure_IsCommandErrorAndPreservesCompilerDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"perfagent-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Broken.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(
                Path.Combine(root, "Broken.cs"),
                """
                public class Broken
                {
                    public int Work() => MissingType.Value;
                }
                """);

            var result = await new ProjectRunner().ValidateAsync(projectPath);

            Assert.Equal(2, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.Validation));
            Assert.Contains("MissingType", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
