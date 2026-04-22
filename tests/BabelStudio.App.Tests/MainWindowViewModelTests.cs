using BabelStudio.App.ViewModels;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Application.Transcripts;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Transcript;
using BabelStudio.Domain.Translation;
using BabelStudio.Media.Playback;

namespace BabelStudio.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ApplyProjectState_marks_stale_translation_segments()
    {
        var viewModel = new MainWindowViewModel();
        TranscriptProjectState state = CreateState(
            transcriptLanguage: "en",
            targetLanguage: "es",
            translatedText: "Hola",
            staleTranslatedSegmentIndices: new HashSet<int> { 0 });

        viewModel.ApplyProjectState(state, @"D:\Dev\BabelStudio\TestProject.babelstudio");

        TranscriptSegmentItem segment = Assert.Single(viewModel.Segments);
        Assert.True(segment.IsTranslationStale);
        Assert.Equal("Stale translation", segment.TranslationStatusLabel);
        Assert.Equal("es", viewModel.SelectedTranslationTargetLanguageCode);
    }

    [Fact]
    public void ShowTranslatedSubtitles_switches_overlay_to_translation_text()
    {
        var viewModel = new MainWindowViewModel();
        TranscriptProjectState state = CreateState(
            transcriptLanguage: "en",
            targetLanguage: "fr",
            translatedText: "Bonjour");

        viewModel.ApplyProjectState(state, @"D:\Dev\BabelStudio\TestProject.babelstudio");
        viewModel.ApplyPlaybackSnapshot(new PlaybackSnapshot(
            IsLoaded: true,
            IsPlaying: false,
            Position: TimeSpan.FromSeconds(1),
            Duration: TimeSpan.FromSeconds(12),
            PlaybackRate: 1d,
            WarningMessage: null));

        Assert.Equal("Hello", viewModel.CurrentSubtitleText);

        viewModel.ShowTranslatedSubtitles = true;

        Assert.Equal("Bonjour", viewModel.CurrentSubtitleText);
    }

    private static TranscriptProjectState CreateState(
        string transcriptLanguage,
        string targetLanguage,
        string translatedText,
        IReadOnlySet<int>? staleTranslatedSegmentIndices = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var project = new BabelProject(Guid.NewGuid(), "Test Project", now, now);
        var mediaAsset = new MediaAsset(
            Guid.NewGuid(),
            project.Id,
            @"D:\media\sample.mp4",
            "sample.mp4",
            "sha256",
            1024,
            now,
            "mov,mp4",
            12d,
            HasAudio: true,
            HasVideo: true,
            now);
        var sourceReference = new SourceMediaReference(
            mediaAsset.SourceFilePath,
            mediaAsset.SourceFileName,
            new FileFingerprint("sha256", mediaAsset.SourceSizeBytes, now),
            new MediaProbeSnapshot(
                "mov,mp4",
                "QuickTime / MOV",
                mediaAsset.DurationSeconds,
                mediaAsset.SourceSizeBytes,
                [ new MediaAudioStream(0, "aac", 2, 48000, mediaAsset.DurationSeconds) ],
                [ new MediaVideoStream(1, "h264", 1920, 1080, 24d, mediaAsset.DurationSeconds) ]),
            now);
        TranscriptRevision transcriptRevision = TranscriptRevision.Create(project.Id, stageRunId: null, revisionNumber: 1, now);
        TranscriptSegment transcriptSegment = TranscriptSegment.Create(transcriptRevision.Id, 0, 0d, 5d, "Hello");
        TranslationRevision translationRevision = TranslationRevision.Create(
            project.Id,
            stageRunId: null,
            transcriptRevision.Id,
            targetLanguage,
            revisionNumber: 1,
            now,
            translationProvider: "opus-mt",
            modelId: "test-model",
            executionProvider: "cpu");
        TranslatedSegment translatedSegment = TranslatedSegment.Create(
            translationRevision.Id,
            0,
            0d,
            5d,
            translatedText,
            sourceSegmentHash: "hash-0");

        return new TranscriptProjectState(
            new OpenProjectResult(
                project,
                mediaAsset,
                sourceReference,
                SourceMediaStatus.Available,
                SourceStatusMessage: null,
                Artifacts:
                [
                    new ProjectArtifact(
                        Guid.NewGuid(),
                        project.Id,
                        mediaAsset.Id,
                        ArtifactKind.TranscriptRevision,
                        "artifacts/transcript/revision-1.json",
                        "sha256",
                        128,
                        DurationSeconds: null,
                        SampleRate: null,
                        ChannelCount: null,
                        now)
                ],
                transcriptLanguage),
            transcriptRevision,
            [ transcriptSegment ],
            translationRevision,
            [ translatedSegment ],
            IsTranslationStale: staleTranslatedSegmentIndices?.Count > 0,
            transcriptLanguage,
            StageRuns:
            [
                StageRunRecord.Start(project.Id, "translation", now)
                    .WithRuntimeInfo("auto", "cpu", "test-model", "test-alias", "merged-decoder", "none")
                    .Complete(now.AddSeconds(1))
            ],
            SupportedTargetLanguages:
            [
                new TranslationTargetLanguageOption(targetLanguage, GetLanguageDisplayName(targetLanguage), TranslationRoutingKind.Direct, IsAvailable: true, "Direct Opus-MT")
            ],
            SelectedTranslationTargetLanguage: targetLanguage,
            StaleTranslatedSegmentIndices: staleTranslatedSegmentIndices ?? new HashSet<int>(),
            WaveformSummary: new WaveformSummary(2, 48000, 1, 12d, [ 0.2f, 0.4f ]));
    }

    private static string GetLanguageDisplayName(string languageCode) =>
        languageCode switch
        {
            "es" => "Spanish",
            "fr" => "French",
            _ => "Translation"
        };
}
