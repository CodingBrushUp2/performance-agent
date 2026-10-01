using PerformanceAgent.Core.Budgets;
using PerformanceAgent.Core.History;

namespace PerformanceAgent.Core.Reporting;

public sealed record PerformanceReportContext(
    ArchivedBenchmarkRun Baseline,
    ArchivedBenchmarkRun Candidate,
    PerformanceBudget Budget,
    string BudgetSource,
    bool Passed);
