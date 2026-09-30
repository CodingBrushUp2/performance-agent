namespace PerformanceAgent.Core.Evidence;

public sealed record EnvironmentComparison(
    bool IsComparable,
    IReadOnlyList<string> Differences);

public sealed class BenchmarkEnvironmentComparer
{
    public EnvironmentComparison Compare(BenchmarkEnvironment? baseline, BenchmarkEnvironment? candidate)
    {
        if (baseline is null || candidate is null)
            return new EnvironmentComparison(
                false,
                ["Environment metadata is missing from baseline or candidate evidence."]);

        var differences = new List<string>();
        AddDifference(differences, "runtime", baseline.Runtime, candidate.Runtime);
        AddDifference(differences, "operating system", baseline.OperatingSystem, candidate.OperatingSystem);
        AddDifference(differences, "architecture", baseline.Architecture, candidate.Architecture);

        return new EnvironmentComparison(differences.Count == 0, differences);
    }

    private static void AddDifference(
        ICollection<string> differences,
        string field,
        string baseline,
        string candidate)
    {
        if (!string.Equals(baseline, candidate, StringComparison.Ordinal))
            differences.Add($"{field}: baseline '{baseline}', candidate '{candidate}'");
    }
}
