using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.Inference.Pipelines.Transcript;

public sealed class ScriptedAudioTranscriptionEngine : IAudioTranscriptionEngine
{
    private static readonly string[] Phrases =
    [
        "Welcome to the first Babel Studio transcript draft.",
        "This placeholder segment proves the transcript slice can reopen without recomputing.",
        "Edits to this text should persist as a new transcript revision.",
        "Later milestones will replace this scripted engine with the Windows ML path.",
        "The current milestone is focused on transcript flow, provenance, and persistence.",
        "This segment exists so longer media produces multiple transcript rows."
    ];

    public Task<IReadOnlyList<RecognizedTranscriptSegment>> TranscribeAsync(
        string normalizedAudioPath,
        IReadOnlyList<SpeechRegion> regions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RecognizedTranscriptSegment[] segments = regions
            .OrderBy(region => region.Index)
            .Select(region => new RecognizedTranscriptSegment(
                region.Index,
                region.StartSeconds,
                region.EndSeconds,
                BuildText(region.Index)))
            .ToArray();

        return Task.FromResult<IReadOnlyList<RecognizedTranscriptSegment>>(segments);
    }

    private static string BuildText(int index)
    {
        string phrase = Phrases[index % Phrases.Length];
        return $"Segment {index + 1}. {phrase}";
    }
}
