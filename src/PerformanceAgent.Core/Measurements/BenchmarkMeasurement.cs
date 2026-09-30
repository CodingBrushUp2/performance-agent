namespace PerformanceAgent.Core.Measurements;

public sealed record BenchmarkMeasurement(
    string Name,
    double MeanNanoseconds,
    long? AllocatedBytesPerOperation);
