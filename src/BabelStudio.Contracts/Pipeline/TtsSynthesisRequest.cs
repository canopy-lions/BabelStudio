namespace BabelStudio.Contracts.Pipeline;

public sealed record TtsSynthesisRequest(
    string Text,
    string LanguageCode,
    VoiceCatalogEntry Voice,
    float Speed = 1.0f,
    string? PhonemeOverride = null);
