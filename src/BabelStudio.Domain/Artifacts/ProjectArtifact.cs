namespace BabelStudio.Domain.Artifacts;

public enum ArtifactKind
{
    Unknown = 0,
    NormalizedAudio = 1,
    WaveformSummary = 2,
    SpeechRegions = 3,
    TranscriptRevision = 4,
    TranslationRevision = 5,
    ReferenceClip = 6
}

public sealed record ProjectArtifact(
    Guid Id,
    Guid ProjectId,
    Guid MediaAssetId,
    ArtifactKind Kind,
    string RelativePath,
    string Sha256,
    long SizeBytes,
    double? DurationSeconds,
    int? SampleRate,
    int? ChannelCount,
    DateTimeOffset CreatedAtUtc,
    Guid? StageRunId = null,
    string? Provenance = null);
