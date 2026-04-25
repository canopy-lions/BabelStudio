namespace BabelStudio.Contracts.Pipeline;

public sealed record TtsSynthesisResult(
    byte[] WavBytes,
    int DurationSamples,
    int SampleRate,
    string ModelId,
    string VoiceId,
    string Provider);
