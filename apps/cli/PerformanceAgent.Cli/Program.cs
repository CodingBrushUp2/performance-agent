using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.Reporting;

return Run(args);

static int Run(string[] args)
{
    if (args.Length != 7 || !string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            "Usage: perfagent compare <name> <baseline-ns> <candidate-ns> <baseline-bytes> <candidate-bytes> <json|markdown>");
        return 2;
    }

    if (!TryParse(args[2], out var baselineNs)
        || !TryParse(args[3], out var candidateNs)
        || !TryParse(args[4], out var baselineBytes)
        || !TryParse(args[5], out var candidateBytes))
    {
        Console.Error.WriteLine("Measurements must be non-negative numbers.");
        return 2;
    }

    try
    {
        var baseline = new BenchmarkMeasurement(args[1], baselineNs, checked((long)baselineBytes));
        var candidate = new BenchmarkMeasurement(args[1], candidateNs, checked((long)candidateBytes));
        var comparison = new BenchmarkComparer().Compare(baseline, candidate);
        var report = new PerformanceReport("1.0", DateTimeOffset.UtcNow, [comparison]);

        var output = args[6].ToLowerInvariant() switch
        {
            "json" => new JsonPerformanceReportWriter().Write(report),
            "markdown" => new MarkdownPerformanceReportWriter().Write(report),
            _ => throw new ArgumentException("Format must be 'json' or 'markdown'.")
        };

        Console.WriteLine(output);
        return 0;
    }
    catch (Exception exception) when (exception is ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
}

static bool TryParse(string value, out double result)
{
    return double.TryParse(
        value,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture,
        out result)
        && double.IsFinite(result)
        && result >= 0;
}
