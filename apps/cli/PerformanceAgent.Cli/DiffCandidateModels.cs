namespace PerformanceAgent.Cli;

internal sealed record ChangedLineRange(int StartLine, int LineCount)
{
    public int EndLine => checked(StartLine + LineCount - 1);
}

internal sealed record DiffFileChange(
    string Path,
    IReadOnlyList<ChangedLineRange> ChangedRanges);

internal sealed record SourceCandidate(
    string FilePath,
    string TypeName,
    string MemberName,
    int StartLine,
    int EndLine,
    int ChangedLineCount,
    string Reason);

internal sealed record CandidateAnalysisResult(
    string BaseRef,
    string HeadRef,
    int ChangedCSharpFileCount,
    int EligibleCSharpFileCount,
    IReadOnlyList<SourceCandidate> Candidates);
