using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

public sealed class CiCanaryTests
{
    [Fact]
    public void Canary_proves_cli_integration_tests_execute_in_ci() => Assert.Fail("Temporary canary: CLI integration tests are discovered and executed.");
}
