namespace BabelStudio.Domain;

public enum StageRunStatus
{
    Running,
    Completed,
    Failed
}

public sealed record StageRunRuntimeInfo
{
    public StageRunRuntimeInfo(
        string requestedProvider,
        string selectedProvider,
        string? modelId = null,
        string? modelAlias = null,
        string? modelVariant = null,
        string? bootstrapDetail = null)
    {
        RequestedProvider = NormalizeRequired(requestedProvider, nameof(requestedProvider));
        SelectedProvider = NormalizeRequired(selectedProvider, nameof(selectedProvider));
        ModelId = NormalizeOptional(modelId);
        ModelAlias = NormalizeOptional(modelAlias);
        ModelVariant = NormalizeOptional(modelVariant);
        BootstrapDetail = NormalizeOptional(bootstrapDetail);
    }

    public string RequestedProvider { get; init; }

    public string SelectedProvider { get; init; }

    public string? ModelId { get; init; }

    public string? ModelAlias { get; init; }

    public string? ModelVariant { get; init; }

    public string? BootstrapDetail { get; init; }

    private static string NormalizeRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Provider is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record StageRunRecord(
    Guid Id,
    Guid ProjectId,
    string StageName,
    StageRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason,
    StageRunRuntimeInfo? RuntimeInfo = null)
{
    public static StageRunRecord Start(Guid projectId, string stageName, DateTimeOffset startedAtUtc)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(stageName))
        {
            throw new ArgumentException("Stage name is required.", nameof(stageName));
        }

        return new StageRunRecord(Guid.NewGuid(), projectId, stageName.Trim(), StageRunStatus.Running, startedAtUtc, null, null);
    }

    public StageRunRecord WithRuntimeInfo(StageRunRuntimeInfo? runtimeInfo) =>
        this with
        {
            RuntimeInfo = runtimeInfo
        };

    public StageRunRecord WithRuntimeInfo(
        string requestedProvider,
        string selectedProvider,
        string? modelId = null,
        string? modelAlias = null,
        string? modelVariant = null,
        string? bootstrapDetail = null) =>
        WithRuntimeInfo(new StageRunRuntimeInfo(
            requestedProvider,
            selectedProvider,
            modelId,
            modelAlias,
            modelVariant,
            bootstrapDetail));
    }

    public StageRunRecord Complete(DateTimeOffset completedAtUtc)
    {
        EnsureCompletionTime(completedAtUtc);
        return this with
        {
            Status = StageRunStatus.Completed,
            CompletedAtUtc = completedAtUtc,
            FailureReason = null
        };
    }

    public StageRunRecord Fail(DateTimeOffset completedAtUtc, string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));
        }

        EnsureCompletionTime(completedAtUtc);
        return this with
        {
            Status = StageRunStatus.Failed,
            CompletedAtUtc = completedAtUtc,
            FailureReason = failureReason.Trim()
        };
    }

    private void EnsureCompletionTime(DateTimeOffset completedAtUtc)
    {
        if (completedAtUtc < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc), "Completion time cannot be earlier than the start time.");
        }
    }
}
