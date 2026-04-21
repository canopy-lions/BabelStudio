namespace BabelStudio.Contracts.Pipeline;

public interface IAudioTranscriptionEngine
{
    Task<IReadOnlyList<RecognizedTranscriptSegment>> TranscribeAsync(
        string normalizedAudioPath,
        IReadOnlyList<SpeechRegion> regions,
        CancellationToken cancellationToken);
}
