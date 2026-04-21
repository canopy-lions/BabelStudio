namespace BabelStudio.Contracts.Pipeline;

public sealed record SpeechRegion(
    int Index,
    double StartSeconds,
    double EndSeconds);
