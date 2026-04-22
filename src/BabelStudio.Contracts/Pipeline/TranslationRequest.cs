namespace BabelStudio.Contracts.Pipeline;

public sealed record TranslationRequest(
    string SourceLanguage,
    string TargetLanguage,
    IReadOnlyList<TranslationInputSegment> Segments,
    bool CommercialSafeMode = true,
    string? PreferredModelAlias = null,
    string? ResolvedModelEntryPath = null);

public sealed record TranslationInputSegment(
    int Index,
    double StartSeconds,
    double EndSeconds,
    string Text);

public sealed record TranslatedTextSegment(
    int Index,
    double StartSeconds,
    double EndSeconds,
    string Text);
