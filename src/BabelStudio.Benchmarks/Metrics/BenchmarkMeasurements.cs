namespace BabelStudio.Benchmarks.Metrics;

internal sealed record BenchmarkMeasurements(
    double ColdLoadMilliseconds,
    double WarmupMilliseconds,
    double WarmLatencyMilliseconds);
