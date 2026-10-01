using Xunit;

namespace PerformanceAgent.AI.Tests;

public sealed class CiCanaryTests
{
    [Fact]
    public void Canary_proves_ai_tests_are_executed_in_ci() => Assert.Fail("Temporary canary: AI test project is discovered and executed.");
}
