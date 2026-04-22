namespace BabelStudio.Inference.Onnx.Translation;

internal sealed record TranslationLanguageDefinition(
    string Code,
    string DisplayName,
    string MadladTag);

internal static class TranslationLanguageCoverageMatrix
{
    private static readonly TranslationLanguageDefinition English = new("en", "English", "eng");
    private static readonly TranslationLanguageDefinition Spanish = new("es", "Spanish", "spa");
    private static readonly TranslationLanguageDefinition French = new("fr", "French", "fra");
    private static readonly TranslationLanguageDefinition German = new("de", "German", "deu");
    private static readonly TranslationLanguageDefinition Italian = new("it", "Italian", "ita");
    private static readonly TranslationLanguageDefinition Portuguese = new("pt", "Portuguese", "por");
    private static readonly TranslationLanguageDefinition Japanese = new("ja", "Japanese", "jpn");

    private static readonly IReadOnlyDictionary<string, TranslationLanguageDefinition> Languages =
        new Dictionary<string, TranslationLanguageDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [English.Code] = English,
            [Spanish.Code] = Spanish,
            [French.Code] = French,
            [German.Code] = German,
            [Italian.Code] = Italian,
            [Portuguese.Code] = Portuguese,
            [Japanese.Code] = Japanese
        };

    public static IReadOnlyList<TranslationLanguageDefinition> GetTargets(string sourceLanguage)
    {
        string normalizedSource = Normalize(sourceLanguage)
            ?? throw new InvalidOperationException("Source language is required.");

        return normalizedSource switch
        {
            "en" => [Spanish, French, German, Italian, Portuguese, Japanese],
            "es" => [English, French, German, Italian, Portuguese, Japanese],
            _ => []
        };
    }

    public static IReadOnlyList<TranslationLanguageDefinition> AllLanguages { get; } =
        Languages.Values
            .OrderBy(language => language.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool TryGetLanguage(string? languageCode, out TranslationLanguageDefinition? definition)
    {
        if (Normalize(languageCode) is not string normalizedLanguageCode)
        {
            definition = null;
            return false;
        }

        return Languages.TryGetValue(normalizedLanguageCode, out definition);
    }

    private static string? Normalize(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? null
            : languageCode.Trim().ToLowerInvariant();
}
