namespace BabelStudio.Benchmarks.Reports;

internal sealed record BenchmarkReport(
    string ModelPath,
    string ReportPath,
    string RequestedProvider,
    string SelectedProvider,
    IReadOnlyList<string> AvailableProviders,
    int DeviceId,
    string InputSource,
    int MeasuredRunCount,
    double ColdLoadMilliseconds,
    double WarmupMilliseconds,
    double WarmLatencyAverageMilliseconds,
    double WarmLatencyMinimumMilliseconds,
    double WarmLatencyMaximumMilliseconds,
    IReadOnlyList<double> WarmLatencySamplesMilliseconds,
    IReadOnlyList<BenchmarkInputSummary> Inputs,
    IReadOnlyList<BenchmarkOutputSummary> Outputs,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc);

internal sealed record BenchmarkInputSummary(
    string Name,
    string ElementType,
    IReadOnlyList<long> Dimensions,
    long ElementCount);

internal sealed record BenchmarkOutputSummary(
    string Name,
    string ElementType,
    IReadOnlyList<long> Dimensions);
