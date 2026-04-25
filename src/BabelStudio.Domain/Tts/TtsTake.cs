namespace BabelStudio.Domain.Tts;

public sealed record TtsTake(
    Guid Id,
    Guid ProjectId,
    Guid VoiceAssignmentId,
    Guid? TranslatedSegmentId,
    int SegmentIndex,
    string? TranslatedTextHash,
    Guid? ArtifactId,
    Guid? StageRunId,
    TtsTakeStatus Status,
    bool IsStale,
    int? DurationSamples,
    int? SampleRate,
    string? Provider,
    string? ModelId,
    string? VoiceId,
    double? DurationOverrunRatio,
    DateTimeOffset CreatedAtUtc)
{
    public static TtsTake Create(
        Guid projectId,
        Guid voiceAssignmentId,
        Guid? translatedSegmentId = null,
        int segmentIndex = 0,
        string? translatedTextHash = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (voiceAssignmentId == Guid.Empty)
        {
            throw new ArgumentException("Voice assignment id is required.", nameof(voiceAssignmentId));
        }

        if (segmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex), "Segment index cannot be negative.");
        }

        return new TtsTake(
            Guid.NewGuid(),
            projectId,
            voiceAssignmentId,
            translatedSegmentId,
            segmentIndex,
            string.IsNullOrWhiteSpace(translatedTextHash) ? null : translatedTextHash.Trim(),
            ArtifactId: null,
            StageRunId: null,
            TtsTakeStatus.Pending,
            IsStale: false,
            DurationSamples: null,
            SampleRate: null,
            Provider: null,
            ModelId: null,
            VoiceId: null,
            DurationOverrunRatio: null,
            DateTimeOffset.UtcNow);
    }

    public TtsTake MarkStale() =>
        this with { IsStale = true, Status = TtsTakeStatus.Stale };

    public TtsTake Complete(Guid artifactId, int durationSamples, int sampleRate, string provider) =>
        Complete(
            artifactId,
            StageRunId,
            durationSamples,
            sampleRate,
            provider,
            ModelId,
            VoiceId,
            DurationOverrunRatio);

    public TtsTake Complete(
        Guid artifactId,
        Guid? stageRunId,
        int durationSamples,
        int sampleRate,
        string provider,
        string? modelId,
        string? voiceId,
        double? durationOverrunRatio) =>
        this with
        {
            ArtifactId = artifactId,
            StageRunId = stageRunId,
            DurationSamples = durationSamples,
            SampleRate = sampleRate,
            Provider = provider,
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim(),
            VoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim(),
            DurationOverrunRatio = durationOverrunRatio,
            Status = TtsTakeStatus.Completed,
            IsStale = false
        };

    // Failed is a terminal status; clearing IsStale avoids the ambiguous
    // "Failed but also stale" combination and matches how Complete() behaves.
    public TtsTake Fail() =>
        this with { Status = TtsTakeStatus.Failed, IsStale = false };
}
