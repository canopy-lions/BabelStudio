using BabelStudio.Domain;

namespace BabelStudio.Benchmarks;

public sealed record BenchmarkBatchReport(
    string RequestedReference,
    string ReportPath,
    IReadOnlyList<BenchmarkReport> Results,
    DateTimeOffset GeneratedAtUtc);
