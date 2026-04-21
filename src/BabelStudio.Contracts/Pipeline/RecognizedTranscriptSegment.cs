namespace BabelStudio.Contracts.Pipeline;

public sealed record RecognizedTranscriptSegment(
    int Index,
    double StartSeconds,
    double EndSeconds,
    string Text);
