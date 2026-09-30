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
            var store = new FileBaselineStore(root);
            var previous = await store.GetAsync(kind);
            await store.SetAsync(kind, args[2]);

            var eventType = previous is null
                ? BaselineEventType.Created
                : BaselineEventType.Reset;
            var timestamp = DateTimeOffset.UtcNow;
            var eventId = $"event-{Guid.NewGuid():N}";
            await new FileBaselineEventStore(root).AppendAsync(
                new BaselineEvent(
                    eventId,
                    timestamp,
                    kind,
                    eventType,
                    args[2],
                    previous?.RunId,
                    "CLI baseline selection"));

            Console.WriteLine($"{kind} baseline: {args[2]}");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (args.Length == 1 && string.Equals(args[0], "history", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
            var archive = new FileRunArchive(root);
            var baselines = new FileBaselineStore(root);
            var current = await baselines.GetAsync(BaselineKind.Current);
            var anchor = await baselines.GetAsync(BaselineKind.Anchor);
            var runs = await archive.ListAsync();

            if (runs.Count == 0)
            {
                Console.WriteLine("No archived benchmark runs.");
                return 0;
            }

            foreach (var run in runs)
            {
                var labels = new List<string>();
                if (string.Equals(run.RunId, current?.RunId, StringComparison.Ordinal))
                    labels.Add("current");
                if (string.Equals(run.RunId, anchor?.RunId, StringComparison.Ordinal))
                    labels.Add("anchor");
                var suffix = labels.Count == 0 ? string.Empty : $" [{string.Join(", ", labels)}]";
                Console.WriteLine($"{run.RunId}  {run.Timestamp:O}  {run.CommitSha ?? "-"}{suffix}");
            }

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
            "Usage: perfagent run <benchmark.csproj> [--output <evidence.json>] | perfagent baseline <set|anchor> <run-id> | perfagent history | perfagent check <baseline.json> <candidate.json> (--budget <budget.json> | <max-mean-regression-%> <max-allocation-regression-%>) | perfagent check [--baseline <baseline.json> | --rid <run-id>] --candidate <candidate.json> (--budget <budget.json> | <max-mean-regression-%> <max-allocation-regression-%>) | perfagent compare <name> <baseline-ns> <candidate-ns> <baseline-bytes> <candidate-bytes> <json|markdown>");
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
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();

        for (var index = 1; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(args[index]);
                continue;
            }

            var option = args[index];
            if (option is not ("--rid" or "--baseline" or "--candidate" or "--budget"))
            {
                Console.Error.WriteLine($"Unknown option '{option}'.");
                return 2;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Option '{option}' requires a value.");
                return 2;
            }

            if (!options.TryAdd(option, args[++index]))
            {
                Console.Error.WriteLine($"Option '{option}' may only be specified once.");
                return 2;
            }
        }

        if (options.ContainsKey("--rid") && options.ContainsKey("--baseline"))
        {
            Console.Error.WriteLine("Use either --rid or --baseline, not both.");
            return 2;
        }

        string baselinePath;
        string candidatePath;
        if (options.TryGetValue("--candidate", out var candidateOption))
        {
            candidatePath = candidateOption;
            if (positional.Count != 0)
            {
                Console.Error.WriteLine("Positional arguments cannot be combined with --candidate.");
                return 2;
            }
        }
        else if (options.ContainsKey("--rid"))
        {
            if (positional.Count != 1)
            {
                Console.Error.WriteLine("A candidate evidence file is required.");
                return 2;
            }

            candidatePath = positional[0];
        }
        else
        {
            if (options.TryGetValue("--baseline", out var baselineOption))
            {
                baselinePath = baselineOption;
                if (positional.Count != 1)
                {
                    Console.Error.WriteLine("A candidate evidence file is required.");
                    return 2;
                }

                candidatePath = positional[0];
            }
            else
            {
                if (positional.Count < 2)
                {
                    PrintCheckUsage();
                    return 2;
                }

                baselinePath = positional[0];
                candidatePath = positional[1];
                positional.RemoveRange(0, 2);
            }
        }

        PerformanceBudget budget;
        if (options.TryGetValue("--budget", out var budgetPath))
        {
            if (positional.Count != 0)
            {
                Console.Error.WriteLine("Unexpected positional arguments.");
                return 2;
            }

            budget = new JsonPerformanceBudgetReader().Read(File.ReadAllText(budgetPath));
        }
        else
        {
            var thresholds = options.ContainsKey("--rid") || options.ContainsKey("--baseline")
                ? positional
                : positional;
            if (thresholds.Count != 2
                || !TryParse(thresholds[0], out var maxMeanRegression)
                || !TryParse(thresholds[1], out var maxAllocationRegression))
            {
                Console.Error.WriteLine("Provide --budget <budget.json> or two non-negative performance budget percentages.");
                return 2;
            }

            budget = new PerformanceBudget(maxMeanRegression, maxAllocationRegression);
        }

        var reader = new JsonBenchmarkEvidenceReader();
        BenchmarkEvidence baseline;
        if (options.TryGetValue("--rid", out var runId))
        {
            var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
            baseline = new FileRunArchive(root).ReadAsync(runId).GetAwaiter().GetResult().Evidence;
        }
        else
        {
            baseline = reader.Read(File.ReadAllText(options.TryGetValue("--baseline", out var explicitBaseline) ? explicitBaseline : baselinePath));
        }

        var candidate = reader.Read(File.ReadAllText(candidatePath));

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

static void PrintCheckUsage() =>
    Console.Error.WriteLine(
        "Usage: perfagent check [--baseline <baseline.json> | --rid <run-id>] --candidate <candidate.json> (--budget <budget.json> | <max-mean-regression-%> <max-allocation-regression-%>)");


static string FormatBudget(double? threshold, bool exceeded) =>
    threshold is null
        ? " (budget not configured)"
        : $" (budget +{threshold:0.##}%) {(exceeded ? "FAIL" : "PASS")}";

static string FormatChange(MetricChange change) =>
    change.Status == ComparisonStatus.Comparable && change.PercentChange is not null
        ? $"{change.Baseline:0.##} -> {change.Candidate:0.##} ({change.PercentChange:+0.##;-0.##;0}%)"
        : $"{change.Baseline?.ToString() ?? "n/a"} -> {change.Candidate?.ToString() ?? "n/a"} ({change.Status})";
