using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.Calibration;
using PerformanceAgent.Core.Comparison;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.Measurements;
using PerformanceAgent.Core.History;
using PerformanceAgent.Core.Reporting;
using PerformanceAgent.Core.Verdicts;
using PerformanceAgent.Cli;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (PerformanceAgent.Cli.HelpContent.TryRender(args, out var help))
    {
        Console.Write(help);
        return 0;
    }

    if (args.Length > 0 && string.Equals(args[0], "report", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            if (args.Length < 2 || args.Length % 2 != 0)
                throw new ArgumentException("Usage: perfagent report <candidate-run-id> [--baseline <run-id>] [--budget <budget.json>] > report.html");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 2; index < args.Length; index += 2)
            {
                if (args[index] is not ("--baseline" or "--budget") || !options.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException($"Unknown or repeated report option '{args[index]}'.");
            }
            var result = await new PerformanceAgent.Cli.HtmlReportService(PerformanceAgent.Cli.WorkspaceStorage.Resolve())
                .CreateAsync(args[1], options.GetValueOrDefault("--baseline"), options.GetValueOrDefault("--budget"));
            Console.Write(result.Html);
            return result.Passed ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (args.Length == 2 && args[0] == "config" && args[1] == "show")
    {
        try
        {
            var configuration = new PerformanceAgent.Cli.WorkspaceConfiguration(PerformanceAgent.Cli.WorkspaceStorage.Resolve()).Inspect();
            Console.WriteLine($"Workspace configuration: {configuration.Path}");
            Console.WriteLine($"User AI configuration: {configuration.UserPath}");
            Console.WriteLine($"Budget source: {configuration.BudgetSource}");
            Console.WriteLine($"Max mean regression (%): {configuration.Budget.MaxMeanRegressionPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Not configured"}");
            Console.WriteLine($"Max allocation regression (%): {configuration.Budget.MaxAllocationRegressionPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Not configured"}");
            Console.WriteLine($"AI provider: {configuration.AiProvider}");
            Console.WriteLine($"AI provider source: {configuration.AiProviderSource}");
            Console.WriteLine($"AI model: {configuration.AiModel ?? "Not configured"}");
            Console.WriteLine($"AI model source: {configuration.AiModelSource}");
            Console.WriteLine("Precedence: built-in defaults < user AI config < workspace perfagent.json < explicit CLI overrides where supported.");
            Console.WriteLine("Workspace budget remains project policy. Do not store API keys or other secrets in either configuration file.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (args.Length == 0 || (args.Length >= 1 && string.Equals(args[0], "ui", StringComparison.OrdinalIgnoreCase)))
    {
        var openBrowser = args.Length == 0 || !args.Skip(1).Any(x => string.Equals(x, "--no-open", StringComparison.OrdinalIgnoreCase));
        if (args.Length > 2 || (args.Length == 2 && openBrowser))
        {
            Console.Error.WriteLine("Usage: perfagent ui [--no-open]");
            return 2;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try { return await PerformanceAgent.Cli.LocalWebUi.RunAsync(openBrowser, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }

    if (args.Length >= 2 && string.Equals(args[0], "candidates", StringComparison.OrdinalIgnoreCase))
    {
        var baseRef = args[1];
        var headRef = "HEAD";
        var maxCandidates = 5;
        var outputFormat = "text";

        if ((args.Length - 2) % 2 != 0)
        {
            Console.Error.WriteLine("Usage: perfagent candidates <base-ref> [--head <ref>] [--max <1-20>] [--format <text|json>]");
            return 2;
        }

        for (var index = 2; index < args.Length; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];

            if (string.Equals(option, "--head", StringComparison.OrdinalIgnoreCase))
            {
                headRef = value;
            }
            else if (string.Equals(option, "--max", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedMax)
                     && parsedMax is >= 1 and <= 20)
            {
                maxCandidates = parsedMax;
            }
            else if (string.Equals(option, "--format", StringComparison.OrdinalIgnoreCase)
                     && value.ToLowerInvariant() is "text" or "json")
            {
                outputFormat = value.ToLowerInvariant();
            }
            else
            {
                Console.Error.WriteLine($"Invalid candidates option '{option}'.");
                return 2;
            }
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var result = await new PerformanceAgent.Cli.GitCandidateService()
                .AnalyzeAsync(baseRef, headRef, maxCandidates, cancellation.Token);

            if (outputFormat == "json")
            {
                Console.WriteLine(new PerformanceAgent.Cli.CandidateJsonWriter().Write(result));
                return 0;
            }

            Console.WriteLine($"Changed C# files: {result.ChangedCSharpFileCount}");
            Console.WriteLine($"Eligible production C# files: {result.EligibleCSharpFileCount}");
            Console.WriteLine($"Candidates: {result.Candidates.Count}");

            if (result.Candidates.Count == 0)
            {
                Console.WriteLine("No changed production methods were found in the requested committed diff.");
            }
            else
            {
                for (var index = 0; index < result.Candidates.Count; index++)
                {
                    var candidate = result.Candidates[index];
                    Console.WriteLine($"{index + 1}. {candidate.TypeName}.{candidate.MemberName}");
                    Console.WriteLine($"   {candidate.FilePath}:{candidate.StartLine}-{candidate.EndLine}");
                    Console.WriteLine($"   {candidate.Reason}");
                }
            }

            Console.WriteLine("Candidates are changed-method focus hints, not performance-risk verdicts.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Candidate discovery cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    if (args.Length >= 2 && string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length is not (2 or 4)
            || (args.Length == 4
                && (!string.Equals(args[2], "--format", StringComparison.OrdinalIgnoreCase)
                    || args[3].ToLowerInvariant() is not ("text" or "json"))))
        {
            Console.Error.WriteLine("Usage: perfagent validate <benchmark.csproj> [--format <text|json>]");
            return 2;
        }

        var outputFormat = args.Length == 4 ? args[3].ToLowerInvariant() : "text";
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var result = await new PerformanceAgent.Cli.ProjectRunner().ValidateAsync(args[1], cancellation.Token);
            Console.Error.Write(result.StandardError);

            if (result.ExitCode == 2 || string.IsNullOrWhiteSpace(result.Validation))
            {
                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                    Console.Error.Write(result.StandardOutput);
                return result.ExitCode;
            }

            if (outputFormat == "json")
            {
                Console.Write(result.Validation);
                return result.ExitCode;
            }

            using var document = System.Text.Json.JsonDocument.Parse(result.Validation);
            var root = document.RootElement;
            var valid = root.GetProperty("valid").GetBoolean();
            Console.WriteLine($"Validation: {(valid ? "VALID" : "INVALID")}");
            Console.WriteLine($"Benchmark types: {root.GetProperty("benchmarkTypeCount").GetInt32()}");

            foreach (var diagnostic in root.GetProperty("diagnostics").EnumerateArray())
            {
                var severity = diagnostic.GetProperty("severity").GetString()?.ToUpperInvariant() ?? "UNKNOWN";
                var source = diagnostic.GetProperty("source").GetString() ?? "Unknown";
                var benchmarkType = diagnostic.GetProperty("benchmarkType").GetString() ?? "Unknown";
                var benchmarkMethod = diagnostic.GetProperty("benchmarkMethod").ValueKind == System.Text.Json.JsonValueKind.Null
                    ? null
                    : diagnostic.GetProperty("benchmarkMethod").GetString();
                var target = benchmarkMethod is null ? benchmarkType : $"{benchmarkType}.{benchmarkMethod}";
                var message = (diagnostic.GetProperty("message").GetString() ?? string.Empty)
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');

                Console.WriteLine($"[{severity}] {source} {target}: {message}");
            }

            return result.ExitCode;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Benchmark validation cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or TimeoutException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    if (args.Length >= 2 && string.Equals(args[0], "calibrate", StringComparison.OrdinalIgnoreCase))
    {
        const int defaultRuns = 3;
        const double defaultMaxSpreadPercent = 5;
        var runCount = defaultRuns;
        var maxSpreadPercent = defaultMaxSpreadPercent;
        var outputFormat = "text";

        for (var index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: perfagent calibrate <benchmark.csproj> [--runs <count>] [--max-spread <percent>] [--format <text|json>]");
                return 2;
            }

            if (string.Equals(args[index], "--runs", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[index + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedRuns)
                && parsedRuns >= 2)
            {
                runCount = parsedRuns;
            }
            else if (string.Equals(args[index], "--max-spread", StringComparison.OrdinalIgnoreCase)
                     && TryParse(args[index + 1], out var parsedSpread))
            {
                maxSpreadPercent = parsedSpread;
            }
            else if (string.Equals(args[index], "--format", StringComparison.OrdinalIgnoreCase)
                     && args[index + 1].ToLowerInvariant() is "text" or "json")
            {
                outputFormat = args[index + 1].ToLowerInvariant();
            }
            else
            {
                Console.Error.WriteLine($"Invalid calibration option '{args[index]}'. Runs must be at least 2, max spread must be a non-negative percentage, and format must be text or json.");
                return 2;
            }
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            var samples = new List<BenchmarkEvidence>(runCount);
            var runIds = new List<string>(runCount);
            var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
            var archive = new FileRunArchive(root);
            var generator = new RunIdGenerator();

            for (var index = 0; index < runCount; index++)
            {
                Console.Error.WriteLine($"Calibration run {index + 1}/{runCount}...");
                var result = await new PerformanceAgent.Cli.ProjectRunner().RunAsync(args[1], cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                Console.Error.Write(result.StandardError);
                if (result.ExitCode != 0)
                    return result.ExitCode;

                var evidence = new JsonBenchmarkEvidenceReader().Read(result.Evidence);
                var timestamp = DateTimeOffset.UtcNow;
                var runId = generator.Create(timestamp);
                await archive.AppendAsync(new ArchivedBenchmarkRun(runId, timestamp, null, evidence), cancellation.Token);
                samples.Add(evidence);
                runIds.Add(runId);
            }

            var calibration = new CalibrationAnalyzer().Analyze(samples, maxSpreadPercent);
            if (outputFormat == "json")
            {
                Console.WriteLine(new PerformanceAgent.Cli.CalibrationJsonWriter().Write(calibration, runIds));
            }
            else
            {
                Console.WriteLine($"Calibration: {(calibration.IsStable ? "STABLE" : "UNSTABLE")}");
                Console.WriteLine($"Runs: {string.Join(", ", runIds)}");
                Console.WriteLine($"Maximum allowed spread: {maxSpreadPercent:0.##}%");
                foreach (var metric in calibration.Metrics)
                {
                    Console.WriteLine(metric.BenchmarkName);
                    Console.WriteLine($"  Mean median: {metric.MedianMeanNanoseconds:0.##} ns; spread: {metric.MeanSpreadPercent:0.##}%");
                    Console.WriteLine(metric.MedianAllocatedBytesPerOperation is null
                        ? "  Allocation: unavailable"
                        : $"  Allocation median: {metric.MedianAllocatedBytesPerOperation} B/op; spread: {metric.AllocationSpreadPercent:0.##}%");
                }
                foreach (var reason in calibration.InstabilityReasons)
                    Console.WriteLine($"  - {reason}");

                Console.WriteLine("Baselines were not changed. Select a baseline explicitly with 'perfagent baseline set <run-id>' or 'perfagent baseline anchor <run-id>'.");
            }

            return calibration.IsStable ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Calibration cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or TimeoutException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    if ((args.Length == 2 || args.Length == 4) && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
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

            var result = await new PerformanceAgent.Cli.ProjectRunner().RunAsync(args[1], cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (result.ExitCode == 0)
            {
                var evidence = new JsonBenchmarkEvidenceReader().Read(result.Evidence);
                var timestamp = DateTimeOffset.UtcNow;
                var runId = new RunIdGenerator().Create(timestamp);
                var archive = new FileRunArchive(Path.Combine(Environment.CurrentDirectory, ".performance-agent"));
                await archive.AppendAsync(new ArchivedBenchmarkRun(runId, timestamp, null, evidence), cancellation.Token);

                if (outputPath is not null)
                {
                    var fullOutputPath = Path.GetFullPath(outputPath);
                    var directory = Path.GetDirectoryName(fullOutputPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(fullOutputPath, result.Evidence, cancellation.Token);
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
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Benchmark run cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
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
            var storage = PerformanceAgent.Cli.WorkspaceStorage.Resolve();
            await new PerformanceAgent.Cli.BaselineSelectionService(storage).SetAsync(
                kind,
                args[2],
                "CLI baseline selection");

            Console.WriteLine($"{kind} baseline: {args[2]}");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (args.Length == 1 && string.Equals(args[0], "storage", StringComparison.OrdinalIgnoreCase))
    {
        var status = PerformanceAgent.Cli.WorkspaceStorage.Resolve().Inspect();
        Console.WriteLine($"Workspace: {status.WorkspaceDirectory}");
        Console.WriteLine($"Storage: {status.StateDirectory}");
        Console.WriteLine($"Writable: {(status.Writable ? "Yes" : "No")}");
        Console.WriteLine("Admin: Not required");
        if (status.Error is not null) Console.Error.WriteLine(status.Error);
        return status.Writable ? 0 : 2;
    }

    if (args.Length == 2 && string.Equals(args[0], "history", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var details = await new PerformanceAgent.Cli.RunDetailsService(PerformanceAgent.Cli.WorkspaceStorage.Resolve()).ReadAsync(args[1]);
            var run = details.Run;
            Console.WriteLine($"RunId: {run.RunId}");
            Console.WriteLine($"Timestamp: {run.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}");
            Console.WriteLine($"Commit: {run.CommitSha ?? "Unavailable"}");
            Console.WriteLine($"Runtime: {run.Evidence.Environment?.Runtime ?? "Unavailable"}");
            Console.WriteLine($"OS: {run.Evidence.Environment?.OperatingSystem ?? "Unavailable"}");
            Console.WriteLine($"Architecture: {run.Evidence.Environment?.Architecture ?? "Unavailable"}");
            Console.WriteLine($"Current: {details.IsCurrent}; Anchor: {details.IsAnchor}");
            foreach (var measurement in run.Evidence.Measurements.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                Console.WriteLine(measurement.Name);
                Console.WriteLine($"  Mean (ns): {measurement.MeanNanoseconds.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)}");
                Console.WriteLine($"  Allocation (B/op): {measurement.AllocatedBytesPerOperation?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unavailable"}");
            }
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
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
            var events = await new FileBaselineEventStore(root).ReadAllAsync();
            _ = new BaselineResolver().Resolve(new BenchmarkHistory("1.0", runs, events));

            if (runs.Count == 0)
            {
                Console.WriteLine("No archived benchmark runs.");
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

            Console.WriteLine();
            Console.WriteLine("Baseline events (append order):");
            if (events.Count == 0)
                Console.WriteLine("No baseline events.");
            foreach (var item in events)
            {
                var timestamp = item.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                Console.WriteLine($"{timestamp}  {item.Kind} {item.Type}  {item.PreviousRunId ?? "-"} -> {item.RunId}  ({item.EventId})");
            }
            Console.WriteLine($"Active Current: {current?.RunId ?? "-"}");
            Console.WriteLine($"Active Anchor: {anchor?.RunId ?? "-"}");

            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    if (string.Equals(args.FirstOrDefault(), "analyze", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: perfagent analyze <candidate-run-id>");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            var result = await new PerformanceAgent.Cli.AnalyzeService(PerformanceAgent.Cli.WorkspaceStorage.Resolve())
                .AnalyzeAsync(args[1], cancellation.Token);
            PerformanceAgent.Cli.AnalysisConsoleWriter.Write(Console.Out, result);
            if (result.AnalysisError is not null)
                Console.Error.WriteLine($"AI analysis failed: {result.AnalysisError}");
            return result.ExitCode;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Analysis cancelled. Benchmark results and baselines were not changed.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    if (string.Equals(args.FirstOrDefault(), "check", StringComparison.OrdinalIgnoreCase))
    {
        return await RunCheckAsync(args);
    }

    if (args.Length != 7 || !string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Unrecognized command or arguments.");
        Console.Error.WriteLine();
        Console.Error.Write(PerformanceAgent.Cli.HelpContent.RenderGeneral());
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
            "html" => new HtmlPerformanceReportWriter().Write(report),
            _ => throw new ArgumentException("Format must be 'json', 'markdown' or 'html'.")
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


static async Task<int> RunCheckAsync(string[] args)
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
            if (option is not ("--rid" or "--run-id" or "-r" or "--baseline" or "-b" or "--candidate" or "-c" or "--budget" or "-p" or "--format"))
            {
                Console.Error.WriteLine($"Unknown option '{option}'.");
                return 2;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Option '{option}' requires a value.");
                return 2;
            }

            var canonicalOption = option switch
            {
                "--rid" or "--run-id" or "-r" => "--run-id",
                "--baseline" or "-b" => "--baseline",
                "--candidate" or "-c" => "--candidate",
                "--budget" or "-p" => "--budget",
                _ => option
            };

            if (!options.TryAdd(canonicalOption, args[++index]))
            {
                Console.Error.WriteLine($"Option '{option}' may only be specified once.");
                return 2;
            }
        }

        if (options.ContainsKey("--run-id") && options.ContainsKey("--baseline"))
        {
            Console.Error.WriteLine("Use either --run-id or --baseline, not both.");
            return 2;
        }

        var outputFormat = (options.GetValueOrDefault("--format") ?? "text").ToLowerInvariant();
        if (outputFormat is not ("text" or "json"))
        {
            Console.Error.WriteLine("Check format must be 'text' or 'json'.");
            return 2;
        }

        string? baselinePath = null;
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
        else if (options.ContainsKey("--run-id"))
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
            else if (options.ContainsKey("--budget") && positional.Count == 1)
            {
                // No explicit baseline: resolve persistent Current/Anchor after reading the candidate.
                candidatePath = positional[0];
                positional.Clear();
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
        else if (positional.Count == 2
                 && TryParse(positional[0], out var maxMeanRegression)
                 && TryParse(positional[1], out var maxAllocationRegression))
        {
            budget = new PerformanceBudget(maxMeanRegression, maxAllocationRegression);
        }
        else if (positional.Count == 0)
        {
            budget = new PerformanceAgent.Cli.WorkspaceConfiguration(
                PerformanceAgent.Cli.WorkspaceStorage.Resolve()).InspectBudget().Budget;
        }
        else
        {
            Console.Error.WriteLine("Provide --budget <budget.json>, two non-negative performance budget percentages, or configure budget in perfagent.json.");
            return 2;
        }

        var reader = new JsonBenchmarkEvidenceReader();
        BenchmarkEvidence baseline;
        CheckVerdictReference baselineReference;
        if (options.TryGetValue("--run-id", out var runId))
        {
            var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
            baseline = (await new FileRunArchive(root).ReadAsync(runId)).Evidence;
            baselineReference = new CheckVerdictReference("runId", runId);
        }
        else if (options.TryGetValue("--baseline", out var explicitBaseline) || baselinePath is not null)
        {
            var source = explicitBaseline ?? baselinePath!;
            baseline = reader.Read(File.ReadAllText(source));
            baselineReference = new CheckVerdictReference("explicit", source);
        }
        else
        {
            baseline = new BenchmarkEvidence("1.0", []);
            baselineReference = new CheckVerdictReference("current", null);
        }

        var candidate = reader.Read(File.ReadAllText(candidatePath));

        if (!options.ContainsKey("--run-id") && !options.ContainsKey("--baseline") && baselinePath is null)
        {
            var root = Path.Combine(Environment.CurrentDirectory, ".performance-agent");
            var baselineStore = new FileBaselineStore(root);
            var currentReference = await baselineStore.GetAsync(BaselineKind.Current);
            if (currentReference is null)
            {
                Console.Error.WriteLine("No current baseline is configured. Provide -b|--baseline, -r|--run-id, or set a current baseline.");
                return 2;
            }

            var archive = new FileRunArchive(root);
            baseline = (await archive.ReadAsync(currentReference.RunId)).Evidence;
            baselineReference = new CheckVerdictReference("current", currentReference.RunId);
            var anchorReference = await baselineStore.GetAsync(BaselineKind.Anchor);
            if (anchorReference is not null
                && !string.Equals(anchorReference.RunId, currentReference.RunId, StringComparison.Ordinal))
            {
                var anchorEvidence = (await archive.ReadAsync(anchorReference.RunId)).Evidence;
                var currentCheck = new PerformanceAgent.Cli.RegressionCheckService().Check(baseline, candidate, budget);
                var anchorCheck = new PerformanceAgent.Cli.RegressionCheckService().Check(anchorEvidence, candidate, budget);
                var overallVerdict = CombineVerdicts(currentCheck.Verdict, anchorCheck.Verdict);

                if (outputFormat == "json")
                {
                    Console.WriteLine(new PerformanceAgent.Cli.CheckVerdictJsonWriter().Write(
                        candidatePath,
                        budget,
                        overallVerdict,
                        [
                            new CheckVerdictInput(
                                new CheckVerdictReference("current", currentReference.RunId),
                                currentCheck),
                            new CheckVerdictInput(
                                new CheckVerdictReference("anchor", anchorReference.RunId),
                                anchorCheck)
                        ]));
                }
                else
                {
                    WriteCheckEvidence("Current", currentCheck, budget);
                    WriteCheckEvidence("Anchor", anchorCheck, budget);
                    Console.WriteLine($"Overall: {VerdictLabel(overallVerdict)}");
                }

                return VerdictExitCode(overallVerdict);
            }
        }

        var check = new PerformanceAgent.Cli.RegressionCheckService().Check(baseline, candidate, budget);
        if (outputFormat == "json")
        {
            Console.WriteLine(new PerformanceAgent.Cli.CheckVerdictJsonWriter().Write(
                candidatePath,
                budget,
                check.Verdict,
                [new CheckVerdictInput(baselineReference, check)]));
        }
        else
        {
            foreach (var item in check.Benchmarks)
            {
                var result = item.Result;
                Console.WriteLine($"{item.Name}: {VerdictLabel(item.Verdict)}");
                Console.WriteLine($"  Mean: {FormatChange(result.Comparison.Mean)}{FormatBudget(budget.MaxMeanRegressionPercent, result.MeanExceeded)}");
                Console.WriteLine($"  Allocation: {FormatChange(result.Comparison.AllocatedBytes)}{FormatBudget(budget.MaxAllocationRegressionPercent, result.AllocationExceeded)}");
            }

            WriteInconclusiveReasons(check);
            Console.WriteLine($"Overall: {VerdictLabel(check.Verdict)}");
        }

        return VerdictExitCode(check.Verdict);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
}

static void WriteCheckEvidence(
    string label,
    PerformanceAgent.Cli.EvidenceCheckResult check,
    PerformanceBudget budget)
{
    Console.WriteLine($"{label} baseline:");
    foreach (var item in check.Benchmarks)
    {
        var result = item.Result;
        Console.WriteLine($"{item.Name}: {VerdictLabel(item.Verdict)}");
        Console.WriteLine($"  Mean: {FormatChange(result.Comparison.Mean)}{FormatBudget(budget.MaxMeanRegressionPercent, result.MeanExceeded)}");
        Console.WriteLine($"  Allocation: {FormatChange(result.Comparison.AllocatedBytes)}{FormatBudget(budget.MaxAllocationRegressionPercent, result.AllocationExceeded)}");
    }

    WriteInconclusiveReasons(check);
}

static void WriteInconclusiveReasons(PerformanceAgent.Cli.EvidenceCheckResult check)
{
    if (check.Verdict != PerformanceVerdict.Inconclusive)
        return;

    foreach (var reason in check.Reasons)
        Console.WriteLine($"  Reason: {reason}");
}

static PerformanceVerdict CombineVerdicts(params PerformanceVerdict[] verdicts) =>
    verdicts.Any(x => x == PerformanceVerdict.Fail)
        ? PerformanceVerdict.Fail
        : verdicts.Any(x => x == PerformanceVerdict.Inconclusive)
            ? PerformanceVerdict.Inconclusive
            : PerformanceVerdict.Pass;

static int VerdictExitCode(PerformanceVerdict verdict) => verdict switch
{
    PerformanceVerdict.Pass => 0,
    PerformanceVerdict.Fail => 1,
    PerformanceVerdict.Inconclusive => 2,
    _ => 2
};

static string VerdictLabel(PerformanceVerdict verdict) => verdict switch
{
    PerformanceVerdict.Pass => "PASS",
    PerformanceVerdict.Fail => "FAIL",
    PerformanceVerdict.Inconclusive => "INCONCLUSIVE",
    _ => "INCONCLUSIVE"
};

static void PrintCheckUsage()
{
    PerformanceAgent.Cli.HelpContent.TryRender(["check", "--help"], out var help);
    Console.Error.Write(help);
}


static string FormatBudget(double? threshold, bool exceeded) => PerformanceAgent.Cli.CheckFormatting.FormatBudget(threshold, exceeded);

static string FormatChange(MetricChange change) => PerformanceAgent.Cli.CheckFormatting.FormatChange(change);
