namespace BabelStudio.Domain.Translation;

public sealed record TranslationRevision(
    Guid Id,
    Guid ProjectId,
    Guid? StageRunId,
    Guid SourceTranscriptRevisionId,
    string TargetLanguage,
    int RevisionNumber,
    DateTimeOffset CreatedAtUtc)
{
    public static TranslationRevision Create(
        Guid projectId,
        Guid? stageRunId,
        Guid sourceTranscriptRevisionId,
        string targetLanguage,
        int revisionNumber,
        DateTimeOffset createdAtUtc)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (stageRunId.HasValue && stageRunId.Value == Guid.Empty)
        {
            throw new ArgumentException("Stage run id must be null or a non-empty GUID.", nameof(stageRunId));
        }

        if (sourceTranscriptRevisionId == Guid.Empty)
        {
            throw new ArgumentException("Source transcript revision id is required.", nameof(sourceTranscriptRevisionId));
        }

        if (revisionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revision number must be positive.");
        }

        return new TranslationRevision(
            Guid.NewGuid(),
            projectId,
            stageRunId,
            sourceTranscriptRevisionId,
            NormalizeLanguageCode(targetLanguage, nameof(targetLanguage)),
            revisionNumber,
            createdAtUtc);
    }

    private static string NormalizeLanguageCode(string? languageCode, string paramName)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new ArgumentException("Language code is required.", paramName);
        }

        return languageCode.Trim().ToLowerInvariant();
    }
}
