namespace BabelStudio.Domain.Tts;

public sealed record TtsTake(
    Guid Id,
    Guid ProjectId,
    Guid VoiceAssignmentId,
    Guid? TranslatedSegmentId,
    Guid? ArtifactId,
    Guid? StageRunId,
    TtsTakeStatus Status,
    bool IsStale,
    int? DurationSamples,
    int? SampleRate,
    string? Provider,
    DateTimeOffset CreatedAtUtc)
{
    public static TtsTake Create(
        Guid projectId,
        Guid voiceAssignmentId,
        Guid? translatedSegmentId = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (voiceAssignmentId == Guid.Empty)
        {
            throw new ArgumentException("Voice assignment id is required.", nameof(voiceAssignmentId));
        }

        return new TtsTake(
            Guid.NewGuid(),
            projectId,
            voiceAssignmentId,
            translatedSegmentId,
            ArtifactId: null,
            StageRunId: null,
            TtsTakeStatus.Pending,
            IsStale: false,
            DurationSamples: null,
            SampleRate: null,
            Provider: null,
            DateTimeOffset.UtcNow);
    }

    public TtsTake MarkStale() =>
        this with { IsStale = true, Status = TtsTakeStatus.Stale };

    public TtsTake Complete(Guid artifactId, int durationSamples, int sampleRate, string provider) =>
        this with
        {
            ArtifactId = artifactId,
            DurationSamples = durationSamples,
            SampleRate = sampleRate,
            Provider = provider,
            Status = TtsTakeStatus.Completed,
            IsStale = false
        };

    public TtsTake Fail() =>
        this with { Status = TtsTakeStatus.Failed };
}
