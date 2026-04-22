namespace BabelStudio.Contracts.Pipeline;

public enum TranslationRoutingKind
{
    Direct = 1,
    Pivot = 2,
    Unavailable = 3
}

public sealed record TranslationTargetLanguageOption(
    string LanguageCode,
    string DisplayName,
    TranslationRoutingKind RoutingKind,
    bool IsAvailable,
    string Detail)
{
    public string DisplayLabel => $"{DisplayName} - {Detail}";
}

public sealed record TranslationRouteSelection(
    string SourceLanguage,
    string TargetLanguage,
    TranslationRoutingKind RoutingKind,
    bool IsAvailable,
    string ProviderName,
    string RouteDetail,
    string? ModelId = null,
    string? PreferredModelAlias = null,
    string? ResolvedModelEntryPath = null,
    string? UnavailableReason = null);

public sealed record TranslationExecutionMetadata(
    string ProviderName,
    string? ModelId,
    string? ModelAlias,
    string? SelectedExecutionProvider,
    TranslationRoutingKind RoutingKind);

public interface ITranslationLanguageRouter
{
    Task<IReadOnlyList<TranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
        string sourceLanguage,
        bool commercialSafeMode,
        CancellationToken cancellationToken);

    Task<TranslationRouteSelection> ResolveRouteAsync(
        string sourceLanguage,
        string targetLanguage,
        bool commercialSafeMode,
        CancellationToken cancellationToken);
}

public interface ITranslationExecutionMetadataReporter
{
    TranslationExecutionMetadata? LastExecutionMetadata { get; }
}
