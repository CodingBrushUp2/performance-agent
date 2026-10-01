namespace PerformanceAgent.Core.Analysis;

public interface IPerformanceAnalysisProvider
{
    Task<PerformanceAnalysis> AnalyzeAsync(
        PerformanceAnalysisRequest request,
        CancellationToken cancellationToken);
}
