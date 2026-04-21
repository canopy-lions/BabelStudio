namespace BabelStudio.Contracts.Pipeline;

public interface ISpeechRegionDetector
{
    Task<IReadOnlyList<SpeechRegion>> DetectAsync(
        string normalizedAudioPath,
        double durationSeconds,
        CancellationToken cancellationToken);
}
