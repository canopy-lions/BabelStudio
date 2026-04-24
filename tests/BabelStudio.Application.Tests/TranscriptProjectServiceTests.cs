using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Application.Transcripts;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Speakers;
using BabelStudio.Domain.Transcript;
using BabelStudio.Domain.Translation;
using BabelStudio.TestDoubles;

namespace BabelStudio.Application.Tests;

public sealed class TranscriptProjectServiceTests : IDisposable
{
    private readonly List<string> tempDirectories = [];

    [Fact]
    public async Task CreateAsync_generates_transcript_revision_and_stage_runs()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState result = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        Assert.NotNull(result.CurrentTranscriptRevision);
        Assert.Equal(1, result.CurrentTranscriptRevision!.RevisionNumber);
        Assert.Equal(3, result.StageRuns.Count);
        Assert.All(result.StageRuns, stageRun => Assert.Equal(StageRunStatus.Completed, stageRun.Status));
        Assert.Equal(2, result.TranscriptSegments.Count);
        Assert.Equal(2, result.Speakers.Count);
        Assert.Equal(2, result.SpeakerTurns.Count);
        Assert.All(result.TranscriptSegments, segment => Assert.NotNull(segment.SpeakerId));
        Assert.Null(result.CurrentTranslationRevision);
        Assert.Null(result.TranscriptLanguage);
        Assert.Contains(result.ProjectState.Artifacts, artifact => artifact.Kind == ArtifactKind.SpeechRegions);
        Assert.Contains(result.ProjectState.Artifacts, artifact => artifact.Kind == ArtifactKind.TranscriptRevision);
    }

    [Fact]
    public async Task SaveEditsAsync_creates_new_revision_without_overwriting_generated_revision()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        TranscriptProjectState saved = await scope.Service.SaveEditsAsync(
            new SaveTranscriptEditsRequest(
                created.CurrentTranscriptRevision!.Id,
                [new EditedTranscriptSegment(created.TranscriptSegments[0].Id, "Edited segment text.", created.TranscriptSegments[0].SpeakerId)]),
            CancellationToken.None);

        Assert.NotNull(saved.CurrentTranscriptRevision);
        Assert.Equal(2, saved.CurrentTranscriptRevision!.RevisionNumber);
        Assert.Equal("Edited segment text.", saved.TranscriptSegments[0].Text);

        TranscriptRevision originalRevision = Assert.Single(scope.TranscriptRepository.Revisions, revision => revision.RevisionNumber == 1);
        IReadOnlyList<TranscriptSegment> originalSegments = scope.TranscriptRepository.SegmentsByRevisionId[originalRevision.Id];
        Assert.Equal("Generated segment 1.", originalSegments[0].Text);
    }

    [Fact]
    public async Task SplitSegmentAsync_creates_two_segments_covering_original_duration()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        TranscriptProjectState split = await scope.Service.SplitSegmentAsync(
            new SplitTranscriptSegmentRequest(created.CurrentTranscriptRevision!.Id, created.TranscriptSegments[0].Id, 2.9),
            CancellationToken.None);

        Assert.Equal(3, split.TranscriptSegments.Count);
        Assert.Equal(0.0, split.TranscriptSegments[0].StartSeconds, 3);
        Assert.Equal(2.9, split.TranscriptSegments[0].EndSeconds, 3);
        Assert.Equal(2.9, split.TranscriptSegments[1].StartSeconds, 3);
        Assert.Equal(5.8, split.TranscriptSegments[1].EndSeconds, 3);
    }

    [Fact]
    public async Task MergeSegmentsAsync_creates_single_segment_spanning_selected_pair()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        TranscriptProjectState merged = await scope.Service.MergeSegmentsAsync(
            new MergeTranscriptSegmentsRequest(
                created.CurrentTranscriptRevision!.Id,
                created.TranscriptSegments[0].Id,
                created.TranscriptSegments[1].Id),
            CancellationToken.None);

        TranscriptSegment mergedSegment = Assert.Single(merged.TranscriptSegments);
        Assert.Equal(0.0, mergedSegment.StartSeconds, 3);
        Assert.Equal(11.8, mergedSegment.EndSeconds, 3);
        Assert.Contains("Generated segment 1.", mergedSegment.Text);
        Assert.Contains("Generated segment 2.", mergedSegment.Text);
    }

    [Fact]
    public async Task TrimSegmentAsync_rejects_overlap_with_adjacent_segment()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.Service.TrimSegmentAsync(
                new TrimTranscriptSegmentRequest(created.CurrentTranscriptRevision!.Id, created.TranscriptSegments[0].Id, 0.0, 6.5),
                CancellationToken.None));
    }

    [Fact]
    public async Task GenerateTranslationAsync_creates_translation_revision_and_transcript_edits_mark_it_needing_refresh()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        TranscriptProjectState languageSet = await scope.Service.SetTranscriptLanguageAsync(
            new SetTranscriptLanguageRequest("en"),
            CancellationToken.None);
        TranscriptProjectState translated = await scope.Service.GenerateTranslationAsync(
            new GenerateTranslationRequest("en", "es", CommercialSafeMode: false),
            CancellationToken.None);

        Assert.Equal("en", languageSet.TranscriptLanguage);
        Assert.NotNull(translated.CurrentTranslationRevision);
        Assert.Equal(1, translated.CurrentTranslationRevision!.RevisionNumber);
        Assert.Equal("opus-mt", translated.CurrentTranslationRevision.TranslationProvider);
        Assert.Equal("fake-opus-en-es", translated.CurrentTranslationRevision.ModelId);
        Assert.False(translated.IsTranslationStale);
        Assert.Equal(2, translated.TranslatedSegments.Count);
        Assert.Equal("Segmento generado 1.", translated.TranslatedSegments[0].Text);
        Assert.All(translated.TranslatedSegments, segment => Assert.False(string.IsNullOrWhiteSpace(segment.SourceSegmentHash)));
        Assert.Contains(translated.ProjectState.Artifacts, artifact => artifact.Kind == ArtifactKind.TranslationRevision);
        Assert.Contains(translated.StageRuns, stageRun => stageRun.StageName == "translation" && stageRun.Status == StageRunStatus.Completed);

        TranscriptProjectState transcriptEdited = await scope.Service.SaveEditsAsync(
            new SaveTranscriptEditsRequest(
                translated.CurrentTranscriptRevision!.Id,
                [new EditedTranscriptSegment(translated.TranscriptSegments[0].Id, "Updated transcript text.", translated.TranscriptSegments[0].SpeakerId)]),
            CancellationToken.None);

        Assert.True(transcriptEdited.IsTranslationStale);
        Assert.NotNull(transcriptEdited.CurrentTranslationRevision);
        Assert.Equal(1, transcriptEdited.CurrentTranslationRevision!.RevisionNumber);
        Assert.Contains(0, transcriptEdited.StaleTranslatedSegmentIndices);
    }

    [Fact]
    public async Task GenerateTranslationAsync_supports_spanish_to_english()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        await scope.Service.SetTranscriptLanguageAsync(
            new SetTranscriptLanguageRequest("es"),
            CancellationToken.None);
        TranscriptProjectState translated = await scope.Service.GenerateTranslationAsync(
            new GenerateTranslationRequest("es", "en", CommercialSafeMode: false),
            CancellationToken.None);

        Assert.NotNull(translated.CurrentTranslationRevision);
        Assert.Equal("en", translated.CurrentTranslationRevision!.TargetLanguage);
        Assert.False(translated.IsTranslationStale);
        Assert.Equal("Generated translation 1.", translated.TranslatedSegments[0].Text);
        Assert.Contains(translated.StageRuns, stageRun => stageRun.StageName == "translation" && stageRun.Status == StageRunStatus.Completed);
    }

    [Fact]
    public async Task SaveTranslationEditsAsync_creates_new_revision_without_overwriting_generated_translation()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);
        await scope.Service.SetTranscriptLanguageAsync(new SetTranscriptLanguageRequest("en"), CancellationToken.None);
        TranscriptProjectState translated = await scope.Service.GenerateTranslationAsync(
            new GenerateTranslationRequest("en", "es", CommercialSafeMode: false),
            CancellationToken.None);

        TranscriptProjectState saved = await scope.Service.SaveTranslationEditsAsync(
            new SaveTranslationEditsRequest(
                translated.CurrentTranslationRevision!.Id,
                "es",
                [new EditedTranslatedSegment(0, "Traduccion editada.")]),
            CancellationToken.None);

        Assert.NotNull(saved.CurrentTranslationRevision);
        Assert.Equal(2, saved.CurrentTranslationRevision!.RevisionNumber);
        Assert.Equal("Traduccion editada.", saved.TranslatedSegments[0].Text);

        TranslationRevision generatedRevision = Assert.Single(scope.TranslationRepository.Revisions, revision => revision.RevisionNumber == 1);
        TranslationRevision editedRevision = Assert.Single(scope.TranslationRepository.Revisions, revision => revision.RevisionNumber == 2);
        Assert.Equal(generatedRevision.SourceTranscriptRevisionId, editedRevision.SourceTranscriptRevisionId);

        IReadOnlyList<TranslatedSegment> generatedSegments = scope.TranslationRepository.SegmentsByRevisionId[generatedRevision.Id];
        Assert.Equal("Segmento generado 1.", generatedSegments[0].Text);
    }

    [Fact]
    public async Task SelectTranslationTargetAsync_switches_to_supported_pivot_target_and_reports_route_status()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);
        TranscriptProjectState transcriptLanguageSet = await scope.Service.SetTranscriptLanguageAsync(
            new SetTranscriptLanguageRequest("en"),
            CancellationToken.None);

        Assert.Contains(transcriptLanguageSet.SupportedTargetLanguages, option => option.LanguageCode == "fr" && option.RoutingKind == TranslationRoutingKind.Pivot);

        TranscriptProjectState frenchTarget = await scope.Service.SelectTranslationTargetAsync(
            new SetTranslationTargetRequest("fr"),
            CancellationToken.None);

        Assert.Equal("fr", frenchTarget.SelectedTranslationTargetLanguage);
        Assert.Null(frenchTarget.CurrentTranslationRevision);
        Assert.Contains(frenchTarget.SupportedTargetLanguages, option => option.LanguageCode == "fr" && option.IsAvailable);
    }

    [Fact]
    public async Task OpenAsync_when_manifest_has_no_transcript_language_returns_unknown_language_state()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        scope.ArtifactStore.Remove(ProjectArtifactPaths.ManifestRelativePath);
        TranscriptProjectState reopened = await scope.Service.OpenAsync(CancellationToken.None);

        Assert.Null(reopened.TranscriptLanguage);
    }

    [Fact]
    public async Task RenameSpeakerAsync_updates_display_name_without_changing_id()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        ProjectSpeaker speaker = created.Speakers[0];
        TranscriptProjectState renamed = await scope.Service.RenameSpeakerAsync(
            new RenameSpeakerRequest(speaker.Id, "Host"),
            CancellationToken.None);

        ProjectSpeaker renamedSpeaker = Assert.Single(renamed.Speakers, candidate => candidate.Id == speaker.Id);
        Assert.Equal("Host", renamedSpeaker.DisplayName);
    }

    [Fact]
    public async Task MergeSpeakersAsync_reassigns_turns_and_deletes_source_speaker()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        ProjectSpeaker target = created.Speakers[0];
        ProjectSpeaker source = created.Speakers[1];
        TranscriptProjectState merged = await scope.Service.MergeSpeakersAsync(
            new MergeSpeakersRequest(source.Id, target.Id),
            CancellationToken.None);

        Assert.Single(merged.Speakers);
        Assert.DoesNotContain(merged.Speakers, speaker => speaker.Id == source.Id);
        Assert.All(merged.SpeakerTurns, turn => Assert.Equal(target.Id, turn.SpeakerId));
        Assert.DoesNotContain(scope.SpeakerRepository.Speakers, speaker => speaker.Id == source.Id);
    }

    [Fact]
    public async Task AssignSpeakerToSegmentAsync_creates_new_revision_with_override()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        Guid targetSpeakerId = created.Speakers[1].Id;
        TranscriptProjectState reassigned = await scope.Service.AssignSpeakerToSegmentAsync(
            new AssignSpeakerToSegmentRequest(
                created.CurrentTranscriptRevision!.Id,
                created.TranscriptSegments[0].Id,
                targetSpeakerId),
            CancellationToken.None);

        Assert.Equal(2, reassigned.CurrentTranscriptRevision!.RevisionNumber);
        Assert.Equal(targetSpeakerId, reassigned.TranscriptSegments[0].SpeakerId);
    }

    [Fact]
    public async Task SplitSpeakerTurnAsync_creates_two_turns()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        SpeakerTurn turn = created.SpeakerTurns[0];
        TranscriptProjectState split = await scope.Service.SplitSpeakerTurnAsync(
            new SplitSpeakerTurnRequest(turn.Id, 2.9),
            CancellationToken.None);

        Assert.Equal(3, split.SpeakerTurns.Count);
        Assert.Contains(split.SpeakerTurns, candidate => Math.Abs(candidate.EndSeconds - 2.9) < 0.001);
        Assert.Contains(split.SpeakerTurns, candidate => Math.Abs(candidate.StartSeconds - 2.9) < 0.001);
    }

    [Fact]
    public async Task ExtractReferenceClipAsync_writes_artifact_and_registers_reference_clip()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory);
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        TranscriptProjectState clipped = await scope.Service.ExtractReferenceClipAsync(
            new ExtractReferenceClipRequest(created.Speakers[0].Id),
            CancellationToken.None);

        ProjectArtifact artifact = Assert.Single(clipped.ProjectState.Artifacts, candidate => candidate.Kind == ArtifactKind.ReferenceClip);
        Assert.True(scope.ArtifactStore.Exists(artifact.RelativePath));
        Assert.Equal(ArtifactKind.ReferenceClip, artifact.Kind);
    }

    [Fact]
    public async Task CreateAsync_when_diarization_fails_falls_back_to_single_speaker_without_throwing()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory, new ThrowingDiarizationEngine());
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        ProjectSpeaker speaker = Assert.Single(created.Speakers);
        Assert.Equal("Speaker 1", speaker.DisplayName);
        Assert.Empty(created.SpeakerTurns);
        Assert.All(created.TranscriptSegments, segment => Assert.Equal(speaker.Id, segment.SpeakerId));
        Assert.Contains(created.StageRuns, stageRun => stageRun.StageName == "diarization" && stageRun.Status == StageRunStatus.Failed);
    }

    [Fact]
    public async Task CreateAsync_propagates_commercial_safe_mode_to_diarization_engine()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        var recordingEngine = new RecordingDiarizationEngine();
        FakeServiceScope scope = CreateScope(tempDirectory, recordingEngine);
        _ = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest(
                "Transcript Demo",
                sourcePath,
                EnableSpeakerDiarization: true,
                CommercialSafeMode: true),
            CancellationToken.None);

        Assert.True(recordingEngine.LastCommercialSafeMode);
    }

    [Fact]
    public async Task CreateAsync_defaults_commercial_safe_mode_to_false_when_not_specified()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        var recordingEngine = new RecordingDiarizationEngine();
        FakeServiceScope scope = CreateScope(tempDirectory, recordingEngine);
        _ = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath),
            CancellationToken.None);

        Assert.False(recordingEngine.LastCommercialSafeMode);
    }

    [Fact]
    public async Task CreateAsync_when_speaker_detection_is_disabled_skips_diarization_and_uses_single_speaker()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        FakeServiceScope scope = CreateScope(tempDirectory, new ThrowingDiarizationEngine());
        TranscriptProjectState created = await scope.Service.CreateAsync(
            new CreateTranscriptProjectRequest("Transcript Demo", sourcePath, EnableSpeakerDiarization: false),
            CancellationToken.None);

        ProjectSpeaker speaker = Assert.Single(created.Speakers);
        Assert.Equal("Speaker 1", speaker.DisplayName);
        Assert.Empty(created.SpeakerTurns);
        Assert.All(created.TranscriptSegments, segment => Assert.Equal(speaker.Id, segment.SpeakerId));
        Assert.DoesNotContain(created.StageRuns, stageRun => stageRun.StageName == "diarization");
    }

    public void Dispose()
    {
        foreach (string directory in tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private FakeServiceScope CreateScope(string tempDirectory, ISpeakerDiarizationEngine? diarizationEngine = null)
    {
        var mediaRepository = new FakeMediaAssetRepository();
        var speakerRepository = new FakeSpeakerRepository();
        var artifactStore = new FakeArtifactStore(Path.Combine(tempDirectory, "project"));
        var transcriptRepository = new FakeTranscriptRepository(speakerRepository);
        var translationRepository = new FakeTranslationRepository();
        var stageRunStore = new FakeProjectStageRunStore();
        var translationLanguageRouter = new FakeTranslationLanguageRouter();
        var translationEngine = new FakeTranslationEngine();
        var service = new TranscriptProjectService(
            new ProjectMediaIngestService(
                new FakeProjectRepository(),
                mediaRepository,
                artifactStore,
                new FakeMediaProbe(),
                new FakeAudioExtractionService(),
                new FakeWaveformSummaryGenerator(),
                new FakeFileFingerprintService()),
            transcriptRepository,
            translationRepository,
            stageRunStore,
            mediaRepository,
            speakerRepository,
            artifactStore,
            new FakeAudioClipExtractor(),
            new FakeFileFingerprintService(),
            new FakeSpeechRegionDetector(),
            diarizationEngine ?? new FakeDiarizationEngine(),
            new FakeAudioTranscriptionEngine(),
            translationLanguageRouter,
            translationEngine);

        return new FakeServiceScope(service, artifactStore, transcriptRepository, translationRepository, translationLanguageRouter, speakerRepository);
    }

    private string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BabelStudio.Application.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        tempDirectories.Add(tempDirectory);
        return tempDirectory;
    }

    private sealed record FakeServiceScope(
        TranscriptProjectService Service,
        FakeArtifactStore ArtifactStore,
        FakeTranscriptRepository TranscriptRepository,
        FakeTranslationRepository TranslationRepository,
        FakeTranslationLanguageRouter TranslationLanguageRouter,
        FakeSpeakerRepository SpeakerRepository);

    private sealed class FakeProjectRepository : IProjectRepository
    {
        private BabelProject? project;

        public Task InitializeAsync(BabelProject project, CancellationToken cancellationToken)
        {
            this.project = project;
            return Task.CompletedTask;
        }

        public Task<BabelProject?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(project);
    }

    private sealed class FakeMediaAssetRepository : IMediaAssetRepository
    {
        private MediaAsset? mediaAsset;

        public List<ProjectArtifact> Artifacts { get; } = [];

        public Task SaveAsync(MediaAsset asset, CancellationToken cancellationToken)
        {
            mediaAsset = asset;
            return Task.CompletedTask;
        }

        public Task UpdateSourcePathAsync(
            Guid mediaAssetId,
            string sourceFilePath,
            string sourceFileName,
            CancellationToken cancellationToken)
        {
            if (mediaAsset is not null && mediaAsset.Id == mediaAssetId)
            {
                mediaAsset = mediaAsset with
                {
                    SourceFilePath = sourceFilePath,
                    SourceFileName = sourceFileName
                };
            }

            return Task.CompletedTask;
        }

        public Task<MediaAsset?> GetPrimaryAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(mediaAsset);

        public Task SaveArtifactAsync(ProjectArtifact artifact, CancellationToken cancellationToken)
        {
            Artifacts.Add(artifact);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProjectArtifact>> GetArtifactsAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProjectArtifact>>(Artifacts.OrderBy(artifact => artifact.CreatedAtUtc).ToArray());
    }

    private sealed class FakeArtifactStore : IArtifactStore
    {
        private readonly Dictionary<string, object> reads = new(StringComparer.OrdinalIgnoreCase);
        private readonly string rootPath;

        public FakeArtifactStore(string rootPath)
        {
            this.rootPath = rootPath;
        }

        public Task EnsureLayoutAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(rootPath);
            foreach (string relativeDirectory in ProjectArtifactPaths.RequiredDirectories)
            {
                Directory.CreateDirectory(GetPath(relativeDirectory));
            }

            return Task.CompletedTask;
        }

        public ArtifactWriteHandle CreateWriteHandle(string relativePath)
        {
            string finalPath = GetPath(relativePath);
            string tempPath = Path.Combine(GetPath("temp"), $"{Guid.NewGuid():N}-{Path.GetFileName(relativePath)}");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            return new ArtifactWriteHandle(relativePath, finalPath, tempPath);
        }

        public Task CommitAsync(ArtifactWriteHandle handle, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(handle.FinalPath)!);
            File.Move(handle.TemporaryPath, handle.FinalPath, overwrite: true);
            return Task.CompletedTask;
        }

        public async Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken cancellationToken)
        {
            string path = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(value), cancellationToken);
            reads[relativePath] = value!;
        }

        public Task<T?> ReadJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
        {
            if (reads.TryGetValue(relativePath, out object? value))
            {
                return Task.FromResult((T?)value);
            }

            return Task.FromResult<T?>(default);
        }

        public void Remove(string relativePath)
        {
            reads.Remove(relativePath);
            string path = GetPath(relativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public string GetPath(string relativePath) => Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public bool Exists(string relativePath) => File.Exists(GetPath(relativePath));
    }

    private sealed class FakeMediaProbe : IMediaProbe
    {
        public Task<MediaProbeSnapshot> ProbeAsync(string sourcePath, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaProbeSnapshot(
                "mov,mp4",
                "QuickTime / MOV",
                12.0,
                1024,
                [new MediaAudioStream(0, "aac", 2, 44100, 12.0)],
                [new MediaVideoStream(1, "h264", 1920, 1080, 24, 12.0)]));
    }

    private sealed class FakeAudioExtractionService : IAudioExtractionService
    {
        public async Task<AudioExtractionResult> ExtractNormalizedAudioAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, [1, 2, 3, 4], cancellationToken);
            return new AudioExtractionResult(destinationPath, 12.0, 48000, 1, 576000);
        }
    }

    private sealed class FakeAudioClipExtractor : IAudioClipExtractor
    {
        public async Task<AudioClipExtractionResult> ExtractAsync(
            string sourceWavePath,
            double startSeconds,
            double endSeconds,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, [11, 22, 33, 44], cancellationToken);
            return new AudioClipExtractionResult(destinationPath, endSeconds - startSeconds, 48000, 1);
        }
    }

    private sealed class FakeWaveformSummaryGenerator : IWaveformSummaryGenerator
    {
        public Task<WaveformSummary> GenerateAsync(string audioPath, CancellationToken cancellationToken) =>
            Task.FromResult(new WaveformSummary(4, 48000, 1, 12.0, [0.1f, 0.5f, 0.3f, 0.2f]));
    }

    private sealed class FakeFileFingerprintService : IFileFingerprintService
    {
        public Task<FileFingerprint> ComputeAsync(string path, CancellationToken cancellationToken)
        {
            long length = File.Exists(path) ? new FileInfo(path).Length : 0;
            return Task.FromResult(new FileFingerprint($"hash-{Path.GetFileName(path)}", length, DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeTranscriptRepository : ITranscriptRepository
    {
        private readonly FakeSpeakerRepository? speakerRepository;

        public FakeTranscriptRepository()
        {
        }

        public FakeTranscriptRepository(FakeSpeakerRepository speakerRepository)
        {
            this.speakerRepository = speakerRepository;
        }

        public List<TranscriptRevision> Revisions { get; } = [];

        public Dictionary<Guid, IReadOnlyList<TranscriptSegment>> SegmentsByRevisionId { get; } = new();

        public Task<TranscriptRevision?> GetCurrentRevisionAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(Revisions.Where(revision => revision.ProjectId == projectId).OrderByDescending(revision => revision.RevisionNumber).FirstOrDefault());

        public Task<IReadOnlyList<TranscriptSegment>> GetSegmentsAsync(Guid transcriptRevisionId, CancellationToken cancellationToken) =>
            Task.FromResult(SegmentsByRevisionId.TryGetValue(transcriptRevisionId, out IReadOnlyList<TranscriptSegment>? segments)
                ? segments
                : (IReadOnlyList<TranscriptSegment>)[]);

        public Task<int> GetNextRevisionNumberAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(Revisions.Where(revision => revision.ProjectId == projectId).Select(revision => revision.RevisionNumber).DefaultIfEmpty(0).Max() + 1);

        public Task SaveRevisionAsync(TranscriptRevision revision, IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken)
        {
            Revisions.Add(revision);
            SegmentsByRevisionId[revision.Id] = segments;
            return Task.CompletedTask;
        }

        public Task ReassignSpeakerAsync(Guid projectId, Guid sourceSpeakerId, Guid targetSpeakerId, CancellationToken cancellationToken)
        {
            foreach (TranscriptRevision revision in Revisions.Where(revision => revision.ProjectId == projectId))
            {
                if (!SegmentsByRevisionId.TryGetValue(revision.Id, out IReadOnlyList<TranscriptSegment>? segments))
                {
                    continue;
                }

                SegmentsByRevisionId[revision.Id] = segments
                    .Select(segment => segment.SpeakerId == sourceSpeakerId
                        ? segment with { SpeakerId = targetSpeakerId }
                        : segment)
                    .ToArray();
            }

            return Task.CompletedTask;
        }

        public async Task ReassignAndMergeSpeakersAsync(Guid projectId, Guid sourceSpeakerId, Guid targetSpeakerId, CancellationToken cancellationToken)
        {
            await ReassignSpeakerAsync(projectId, sourceSpeakerId, targetSpeakerId, cancellationToken).ConfigureAwait(false);
            if (speakerRepository is not null)
            {
                speakerRepository.Turns.RemoveAll(turn => turn.ProjectId == projectId && turn.SpeakerId == sourceSpeakerId);
                speakerRepository.Speakers.RemoveAll(speaker => speaker.ProjectId == projectId && speaker.Id == sourceSpeakerId);
            }
        }
    }

    private sealed class FakeSpeakerRepository : ISpeakerRepository
    {
        public List<ProjectSpeaker> Speakers { get; } = [];

        public List<SpeakerTurn> Turns { get; } = [];

        public Task<IReadOnlyList<ProjectSpeaker>> ListSpeakersAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProjectSpeaker>>(Speakers.Where(speaker => speaker.ProjectId == projectId).OrderBy(speaker => speaker.CreatedAtUtc).ToArray());

        public Task<IReadOnlyList<SpeakerTurn>> ListTurnsAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeakerTurn>>(Turns.Where(turn => turn.ProjectId == projectId).OrderBy(turn => turn.StartSeconds).ToArray());

        public Task<ProjectSpeaker> EnsureDefaultSpeakerAsync(Guid projectId, CancellationToken cancellationToken)
        {
            ProjectSpeaker? existing = Speakers.FirstOrDefault(speaker => speaker.ProjectId == projectId);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            ProjectSpeaker speaker = ProjectSpeaker.Create(projectId, "Speaker 1", DateTimeOffset.UtcNow);
            Speakers.Add(speaker);
            return Task.FromResult(speaker);
        }

        public Task ReplaceDiarizationAsync(Guid projectId, IReadOnlyList<ProjectSpeaker> speakers, IReadOnlyList<SpeakerTurn> turns, CancellationToken cancellationToken)
        {
            Speakers.RemoveAll(speaker => speaker.ProjectId == projectId);
            Speakers.AddRange(speakers);
            Turns.RemoveAll(turn => turn.ProjectId == projectId);
            Turns.AddRange(turns);
            return Task.CompletedTask;
        }

        public Task RenameSpeakerAsync(Guid projectId, Guid speakerId, string displayName, CancellationToken cancellationToken)
        {
            int index = Speakers.FindIndex(speaker => speaker.ProjectId == projectId && speaker.Id == speakerId);
            if (index >= 0)
            {
                Speakers[index] = Speakers[index].Rename(displayName);
            }

            return Task.CompletedTask;
        }

        public Task MergeSpeakersAsync(Guid projectId, Guid sourceSpeakerId, Guid targetSpeakerId, CancellationToken cancellationToken)
        {
            for (int index = 0; index < Turns.Count; index++)
            {
                SpeakerTurn turn = Turns[index];
                if (turn.ProjectId == projectId && turn.SpeakerId == sourceSpeakerId)
                {
                    Turns[index] = turn with { SpeakerId = targetSpeakerId };
                }
            }

            Speakers.RemoveAll(speaker => speaker.ProjectId == projectId && speaker.Id == sourceSpeakerId);
            return Task.CompletedTask;
        }

        public Task SplitTurnAsync(Guid projectId, Guid speakerTurnId, double splitSeconds, CancellationToken cancellationToken)
        {
            int index = Turns.FindIndex(turn => turn.ProjectId == projectId && turn.Id == speakerTurnId);
            if (index < 0)
            {
                throw new InvalidOperationException("Speaker turn was not found.");
            }

            SpeakerTurn original = Turns[index];
            Turns.RemoveAt(index);
            Turns.Insert(index, SpeakerTurn.Create(projectId, original.SpeakerId, original.StartSeconds, splitSeconds, original.Confidence, original.HasOverlap, original.StageRunId));
            Turns.Insert(index + 1, SpeakerTurn.Create(projectId, original.SpeakerId, splitSeconds, original.EndSeconds, original.Confidence, original.HasOverlap, original.StageRunId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTranslationRepository : ITranslationRepository
    {
        public List<TranslationRevision> Revisions { get; } = [];

        public Dictionary<Guid, IReadOnlyList<TranslatedSegment>> SegmentsByRevisionId { get; } = new();

        public Task<TranslationRevision?> GetCurrentRevisionAsync(Guid projectId, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult(Revisions
                .Where(revision => revision.ProjectId == projectId && revision.TargetLanguage == targetLanguage)
                .OrderByDescending(revision => revision.RevisionNumber)
                .FirstOrDefault());

        public Task<IReadOnlyList<TranslatedSegment>> GetSegmentsAsync(Guid translationRevisionId, CancellationToken cancellationToken) =>
            Task.FromResult(SegmentsByRevisionId.TryGetValue(translationRevisionId, out IReadOnlyList<TranslatedSegment>? segments)
                ? segments
                : (IReadOnlyList<TranslatedSegment>)[]);

        public Task<int> GetNextRevisionNumberAsync(Guid projectId, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult(Revisions
                .Where(revision => revision.ProjectId == projectId && revision.TargetLanguage == targetLanguage)
                .Select(revision => revision.RevisionNumber)
                .DefaultIfEmpty(0)
                .Max() + 1);

        public Task SaveRevisionAsync(TranslationRevision revision, IReadOnlyList<TranslatedSegment> segments, CancellationToken cancellationToken)
        {
            Revisions.Add(revision);
            SegmentsByRevisionId[revision.Id] = segments;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjectStageRunStore : IProjectStageRunStore
    {
        private readonly List<StageRunRecord> stageRuns = [];

        public Task CreateAsync(StageRunRecord stageRun, CancellationToken cancellationToken)
        {
            stageRuns.Add(stageRun);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(StageRunRecord stageRun, CancellationToken cancellationToken)
        {
            int index = stageRuns.FindIndex(candidate => candidate.Id == stageRun.Id);
            if (index >= 0)
            {
                stageRuns[index] = stageRun;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StageRunRecord>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StageRunRecord>>(stageRuns.Where(stageRun => stageRun.ProjectId == projectId).OrderBy(stageRun => stageRun.StartedAtUtc).ToArray());
    }

    private sealed class FakeSpeechRegionDetector : ISpeechRegionDetector
    {
        public Task<IReadOnlyList<SpeechRegion>> DetectAsync(string normalizedAudioPath, double durationSeconds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechRegion>>(
            [
                new SpeechRegion(0, 0.0, 5.8),
                new SpeechRegion(1, 6.0, 11.8)
            ]);
    }

    private sealed class FakeAudioTranscriptionEngine : IAudioTranscriptionEngine
    {
        public Task<IReadOnlyList<RecognizedTranscriptSegment>> TranscribeAsync(
            string normalizedAudioPath,
            IReadOnlyList<SpeechRegion> regions,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecognizedTranscriptSegment>>(
            [
                new RecognizedTranscriptSegment(0, regions[0].StartSeconds, regions[0].EndSeconds, "Generated segment 1."),
                new RecognizedTranscriptSegment(1, regions[1].StartSeconds, regions[1].EndSeconds, "Generated segment 2.")
            ]);
    }

    private sealed class ThrowingDiarizationEngine : ISpeakerDiarizationEngine
    {
        public Task<IReadOnlyList<DiarizedSpeakerTurn>> DiarizeAsync(string normalizedAudioPath, double durationSeconds, IReadOnlyList<SpeechRegion> speechRegions, bool commercialSafeMode, CancellationToken cancellationToken) =>
            throw new FileNotFoundException("SortFormer ONNX export was not found.");
    }

    private sealed class RecordingDiarizationEngine : ISpeakerDiarizationEngine
    {
        public bool LastCommercialSafeMode { get; private set; }

        public Task<IReadOnlyList<DiarizedSpeakerTurn>> DiarizeAsync(
            string normalizedAudioPath,
            double durationSeconds,
            IReadOnlyList<SpeechRegion> speechRegions,
            bool commercialSafeMode,
            CancellationToken cancellationToken)
        {
            LastCommercialSafeMode = commercialSafeMode;
            return Task.FromResult<IReadOnlyList<DiarizedSpeakerTurn>>(
            [
                new DiarizedSpeakerTurn("spk_0", 0.0, 1.0, Confidence: 0.9, HasOverlap: false)
            ]);
        }
    }
}
