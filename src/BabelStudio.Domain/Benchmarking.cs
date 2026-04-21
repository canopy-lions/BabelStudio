namespace BabelStudio.Domain;

public sealed record BenchmarkRequest(
    string ModelPath,
    string ReportPath,
    BenchmarkProviderPreference ProviderPreference,
    int RunCount);

public sealed record BenchmarkMeasurements(
    double? ColdLoadMilliseconds,
    double? WarmupMilliseconds,
    double? WarmLatencyAverageMilliseconds,
    double? WarmLatencyMinimumMilliseconds,
    double? WarmLatencyMaximumMilliseconds,
    double? AudioDurationSeconds,
    double? RealTimeFactorAverage);

public sealed record BenchmarkReport(
    string Scenario,
    string ModelPath,
    string ReportPath,
    BenchmarkStatus Status,
    string RequestedProvider,
    string SelectedProvider,
    int RunCount,
    bool SupportsExecution,
    long ModelSizeBytes,
    BenchmarkMeasurements Measurements,
    string? FailureReason,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAtUtc);

public enum BenchmarkProviderPreference
{
    Auto,
    Cpu,
    Dml
}

public enum BenchmarkStatus
{
    Planned,
    Completed,
    Failed
}

public sealed record BenchmarkRunRecord(
    Guid Id,
    string ModelId,
    string ModelPath,
    string ReportPath,
    BenchmarkStatus Status,
    string RequestedProvider,
    string SelectedProvider,
    int RunCount,
    bool SupportsExecution,
    long ModelSizeBytes,
    double? ColdLoadMilliseconds,
    double? WarmLatencyAverageMilliseconds,
    double? WarmLatencyMinimumMilliseconds,
    double? WarmLatencyMaximumMilliseconds,
    string? FailureReason,
    DateTimeOffset GeneratedAtUtc)
{
    public static BenchmarkRunRecord Create(
        string modelId,
        string modelPath,
        string reportPath,
        BenchmarkStatus status,
        string requestedProvider,
        string selectedProvider,
        int runCount,
        bool supportsExecution,
        long modelSizeBytes,
        double? coldLoadMilliseconds,
        double? warmLatencyAverageMilliseconds,
        double? warmLatencyMinimumMilliseconds,
        double? warmLatencyMaximumMilliseconds,
        string? failureReason,
        DateTimeOffset generatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model id is required.", nameof(modelId));
        }

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path is required.", nameof(modelPath));
        }

        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new ArgumentException("Report path is required.", nameof(reportPath));
        }

        if (string.IsNullOrWhiteSpace(requestedProvider))
        {
            throw new ArgumentException("Requested provider is required.", nameof(requestedProvider));
        }

        if (string.IsNullOrWhiteSpace(selectedProvider))
        {
            throw new ArgumentException("Selected provider is required.", nameof(selectedProvider));
        }

        return new BenchmarkRunRecord(
            Guid.NewGuid(),
            modelId.Trim(),
            modelPath.Trim(),
            reportPath.Trim(),
            status,
            requestedProvider.Trim(),
            selectedProvider.Trim(),
            runCount,
            supportsExecution,
            modelSizeBytes,
            coldLoadMilliseconds,
            warmLatencyAverageMilliseconds,
            warmLatencyMinimumMilliseconds,
            warmLatencyMaximumMilliseconds,
            string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim(),
            generatedAtUtc);
    }
}
