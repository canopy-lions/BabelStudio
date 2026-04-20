namespace BabelStudio.Domain;

public enum StageRunStatus
{
    Running,
    Completed,
    Failed
}

public sealed record StageRunRecord(
    Guid Id,
    Guid ProjectId,
    string StageName,
    StageRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason)
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
