namespace BabelStudio.Contracts.Pipeline;

public sealed record VoiceCatalogEntry(
    string VoiceId,
    string LanguageCode,
    string Gender,
    string DisplayName);
