namespace BabelStudio.Application.Projects;

public static class ProjectArtifactPaths
{
    public const string DatabaseFileName = "babel.db";
    public const string ManifestRelativePath = "manifest.json";
    public const string SourceReferenceRelativePath = "media/source-reference.json";
    public const string NormalizedAudioRelativePath = "media/normalized_audio.wav";
    public const string WaveformSummaryRelativePath = "artifacts/waveform/normalized_audio.waveform.json";
    public const string SpeechRegionsRelativePath = "artifacts/audio/speech-regions.json";

    public static readonly string[] RequiredDirectories =
    [
        "media",
        "artifacts",
        "artifacts/audio",
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
}
