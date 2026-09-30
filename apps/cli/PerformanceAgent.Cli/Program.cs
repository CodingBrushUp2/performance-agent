using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Reporting;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if ((args.Length == 2 || args.Length == 4) && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            string? outputPath = null;
            if (args.Length == 4)
            {
                if (!string.Equals(args[2], "--output", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Usage: perfagent run <benchmark.csproj> [--output <evidence.json>]");
                    return 2;
                }

                outputPath = args[3];
            }

            var result = await new PerformanceAgent.Cli.ProjectRunner().RunAsync(args[1]);
            if (result.ExitCode == 0)
            {
                var evidence = new JsonBenchmarkEvidenceReader().Read(result.Evidence);
                var timestamp = DateTimeOffset.UtcNow;
                var runId = new RunIdGenerator().Create(timestamp);
                var archive = new FileRunArchive(Path.Combine(Environment.CurrentDirectory, ".performance-agent"));
                await archive.AppendAsync(new ArchivedBenchmarkRun(runId, timestamp, null, evidence));

                if (outputPath is not null)
                {
                    var fullOutputPath = Path.GetFullPath(outputPath);
                    var directory = Path.GetDirectoryName(fullOutputPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(fullOutputPath, result.Evidence);
                }
                else
                {
                    Console.Write(result.Evidence);
                }

                Console.Error.WriteLine($"Archived run: {runId}");
            }

            Console.Error.Write(result.StandardError);
            return result.ExitCode;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (args.Length == 3
        && string.Equals(args[0], "baseline", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(args[1], "set", StringComparison.OrdinalIgnoreCase)
            || string.Equals(args[1], "anchor", StringComparison.OrdinalIgnoreCase)))
    {
        try
        {
            var kind = string.Equals(args[1], "anchor", StringComparison.OrdinalIgnoreCase)
                ? BaselineKind.Anchor
                : BaselineKind.Current;
            var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
            await new FileBaselineStore(root).SetAsync(kind, args[2]);
            Console.WriteLine($"{kind} baseline: {args[2]}");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (string.Equals(args.FirstOrDefault(), "check", StringComparison.OrdinalIgnoreCase))
    {
        return RunCheck(args);
    }

    if (args.Length != 7 || !string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            "Usage: perfagent run <benchmark.csproj> [--output <evidence.json>] | perfagent baseline <set|anchor> <run-id> | perfagent check <baseline.json> <candidate.json> (--budget <budget.json> | <max-mean-regression-%> <max-allocation-regression-%>) | perfagent compare <name> <baseline-ns> <candidate-ns> <baseline-bytes> <candidate-bytes> <json|markdown>");
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


static int RunCheck(string[] args)
{
    try
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine("Usage: perfagent check <baseline.json> <candidate.json> (--budget <budget.json> | <max-mean-regression-%> <max-allocation-regression-%>)");
            return 2;
        }

        PerformanceBudget budget;
        if (string.Equals(args[3], "--budget", StringComparison.OrdinalIgnoreCase))
        {
            budget = new JsonPerformanceBudgetReader().Read(File.ReadAllText(args[4]));
        }
        else
        {
            if (!TryParse(args[3], out var maxMeanRegression)
                || !TryParse(args[4], out var maxAllocationRegression))
            {
                Console.Error.WriteLine("Performance budget thresholds must be non-negative percentages.");
                return 2;
            }

            budget = new PerformanceBudget(maxMeanRegression, maxAllocationRegression);
        }

        var reader = new JsonBenchmarkEvidenceReader();
        var baseline = reader.Read(File.ReadAllText(args[1]));
        var candidate = reader.Read(File.ReadAllText(args[2]));

        var environmentComparison = new BenchmarkEnvironmentComparer().Compare(
            baseline.Environment,
            candidate.Environment);
        if (!environmentComparison.IsComparable)
        {
            Console.Error.WriteLine("Baseline and candidate benchmark environments are not comparable:");
            foreach (var difference in environmentComparison.Differences)
                Console.Error.WriteLine($"  - {difference}");
            return 2;
        }

        var baselineByName = baseline.Measurements.ToDictionary(measurement => measurement.Name, StringComparer.Ordinal);
        var candidateByName = candidate.Measurements.ToDictionary(measurement => measurement.Name, StringComparer.Ordinal);

        var missingCandidates = baselineByName.Keys.Except(candidateByName.Keys, StringComparer.Ordinal).ToArray();
        var newCandidates = candidateByName.Keys.Except(baselineByName.Keys, StringComparer.Ordinal).ToArray();
        if (missingCandidates.Length > 0 || newCandidates.Length > 0)
        {
            Console.Error.WriteLine("Baseline and candidate benchmark identities do not match.");
            return 2;
        }

        var checker = new PerformanceBudgetChecker();
        var passed = true;

        foreach (var name in baselineByName.Keys.Order(StringComparer.Ordinal))
        {
            var result = checker.Check(baselineByName[name], candidateByName[name], budget);
            passed &= result.Passed;

            Console.WriteLine($"{name}: {(result.Passed ? "PASS" : "FAIL")}");
            Console.WriteLine($"  Mean: {FormatChange(result.Comparison.Mean)}{FormatBudget(budget.MaxMeanRegressionPercent, result.MeanExceeded)}");
            Console.WriteLine($"  Allocation: {FormatChange(result.Comparison.AllocatedBytes)}{FormatBudget(budget.MaxAllocationRegressionPercent, result.AllocationExceeded)}");
        }

        Console.WriteLine($"Overall: {(passed ? "PASS" : "FAIL")}");
        return passed ? 0 : 1;
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
}

static string FormatBudget(double? threshold, bool exceeded) =>
    threshold is null
        ? " (budget not configured)"
        : $" (budget +{threshold:0.##}%) {(exceeded ? "FAIL" : "PASS")}";

static string FormatChange(MetricChange change) =>
    change.Status == ComparisonStatus.Comparable && change.PercentChange is not null
        ? $"{change.Baseline:0.##} -> {change.Candidate:0.##} ({change.PercentChange:+0.##;-0.##;0}%)"
        : $"{change.Baseline?.ToString() ?? "n/a"} -> {change.Candidate?.ToString() ?? "n/a"} ({change.Status})";
