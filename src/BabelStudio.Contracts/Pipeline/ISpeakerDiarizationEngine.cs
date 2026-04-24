namespace BabelStudio.Contracts.Pipeline;

public interface ISpeakerDiarizationEngine
{
    Task<IReadOnlyList<DiarizedSpeakerTurn>> DiarizeAsync(
        string normalizedAudioPath,
        double durationSeconds,
        IReadOnlyList<SpeechRegion> speechRegions,
        CancellationToken cancellationToken);
}

public sealed record DiarizedSpeakerTurn(
    string SpeakerKey,
    double StartSeconds,
    double EndSeconds,
    double? Confidence = null,
    bool HasOverlap = false)
{
    public string NormalizedSpeakerKey =>
        string.IsNullOrWhiteSpace(SpeakerKey)
            ? "speaker-unknown"
            : SpeakerKey.Trim();
}
