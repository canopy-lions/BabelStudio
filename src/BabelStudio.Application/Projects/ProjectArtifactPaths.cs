namespace BabelStudio.Application.Projects;

public static class ProjectArtifactPaths
{
    public const string DatabaseFileName = "babel.db";
    public const string ManifestRelativePath = "manifest.json";
    public const string SourceReferenceRelativePath = "media/source-reference.json";
    public const string NormalizedAudioRelativePath = "media/normalized_audio.wav";
    public const string WaveformSummaryRelativePath = "artifacts/waveform/normalized_audio.waveform.json";
    public const string SpeechRegionsRelativePath = "artifacts/audio/speech-regions.json";
    public const string TranslationDirectoryRelativePath = "artifacts/translation";

    public static readonly string[] RequiredDirectories =
    [
        "media",
        "artifacts",
        "artifacts/audio",
        "artifacts/translation",
        "artifacts/translation/es",
        "artifacts/transcript",
        "artifacts/waveform",
        "logs",
        "temp"
    ];

    public static string GetSpeechRegionsRelativePath() => SpeechRegionsRelativePath;

    public static string GetTranscriptRevisionRelativePath(int revisionNumber)
    {
        if (revisionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revision number must be positive.");
        }

        return $"artifacts/transcript/transcript-revision-{revisionNumber:D4}.json";
    }

    public static string GetTranslationRevisionRelativePath(string targetLanguage, int revisionNumber)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            throw new ArgumentException("Target language is required.", nameof(targetLanguage));
        }

        if (revisionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revision number must be positive.");
        }

        string normalizedTargetLanguage = targetLanguage.Trim().ToLowerInvariant();
        return $"artifacts/translation/{normalizedTargetLanguage}/translation-revision-{revisionNumber:D4}.json";
    }
}
