namespace BabelStudio.Domain.Transcript;

public sealed record TranscriptRevision(
    Guid Id,
    Guid ProjectId,
    Guid? StageRunId,
    int RevisionNumber,
    DateTimeOffset CreatedAtUtc)
{
    public static TranscriptRevision Create(
        Guid projectId,
        Guid? stageRunId,
        int revisionNumber,
        DateTimeOffset createdAtUtc)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (revisionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revision number must be positive.");
        }

        return new TranscriptRevision(
            Guid.NewGuid(),
            projectId,
            stageRunId,
            revisionNumber,
            createdAtUtc);
    }
}
