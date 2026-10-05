using System.Text.RegularExpressions;

namespace PerformanceAgent.Cli;

internal static partial class UnifiedDiffParser
{
    public static IReadOnlyList<DiffFileChange> Parse(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var changes = new Dictionary<string, List<ChangedLineRange>>(StringComparer.Ordinal);
        string? currentPath = null;

        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                currentPath = ParsePath(line[4..]);
                if (currentPath is not null && !changes.ContainsKey(currentPath))
                    changes[currentPath] = [];
                continue;
            }

            if (currentPath is null || !line.StartsWith("@@ ", StringComparison.Ordinal))
                continue;

            var match = HunkHeader().Match(line);
            if (!match.Success)
                continue;

            var start = int.Parse(match.Groups["start"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var count = match.Groups["count"].Success
                ? int.Parse(match.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 1;

            if (count > 0)
                changes[currentPath].Add(new ChangedLineRange(start, count));
        }

        return changes
            .Where(item => item.Value.Count != 0)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new DiffFileChange(item.Key, item.Value.ToArray()))
            .ToArray();
    }

    private static string? ParsePath(string value)
    {
        if (string.Equals(value, "/dev/null", StringComparison.Ordinal))
            return null;

        return value.StartsWith("b/", StringComparison.Ordinal)
            ? value[2..]
            : value;
    }

    [GeneratedRegex("^@@ -\\d+(?:,\\d+)? \\+(?<start>\\d+)(?:,(?<count>\\d+))? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();
}
