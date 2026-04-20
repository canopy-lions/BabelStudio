namespace BabelStudio.Domain.Media;

public sealed record MediaAsset(
    Guid Id,
    Guid ProjectId,
    string SourceFileName,
    string FingerprintSha256,
    long SourceSizeBytes,
    DateTimeOffset SourceLastWriteTimeUtc,
    string FormatName,
    double DurationSeconds,
    bool HasAudio,
    bool HasVideo,
    DateTimeOffset CreatedAtUtc);
