using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Transcript;
using BabelStudio.Domain.Translation;

namespace BabelStudio.Application.Transcripts;

public sealed class TranscriptProjectService
{
    private const string EnglishLanguageCode = "en";
    private const string SpanishLanguageCode = "es";
    private readonly ProjectMediaIngestService projectMediaIngestService;
    private readonly ITranscriptRepository transcriptRepository;
    private readonly ITranslationRepository translationRepository;
    private readonly IProjectStageRunStore stageRunStore;
    private readonly IMediaAssetRepository mediaAssetRepository;
    private readonly IArtifactStore artifactStore;
    private readonly IFileFingerprintService fileFingerprintService;
    private readonly ISpeechRegionDetector speechRegionDetector;
    private readonly IAudioTranscriptionEngine transcriptionEngine;
    private readonly ITranslationEngine translationEngine;

    public TranscriptProjectService(
        ProjectMediaIngestService projectMediaIngestService,
        ITranscriptRepository transcriptRepository,
        ITranslationRepository translationRepository,
        IProjectStageRunStore stageRunStore,
        IMediaAssetRepository mediaAssetRepository,
        IArtifactStore artifactStore,
        IFileFingerprintService fileFingerprintService,
        ISpeechRegionDetector speechRegionDetector,
        IAudioTranscriptionEngine transcriptionEngine,
        ITranslationEngine translationEngine)
    {
        this.projectMediaIngestService = projectMediaIngestService;
        this.transcriptRepository = transcriptRepository;
        this.translationRepository = translationRepository;
        this.stageRunStore = stageRunStore;
        this.mediaAssetRepository = mediaAssetRepository;
        this.artifactStore = artifactStore;
        this.fileFingerprintService = fileFingerprintService;
        this.speechRegionDetector = speechRegionDetector;
        this.transcriptionEngine = transcriptionEngine;
        this.translationEngine = translationEngine;
    }

    public async Task<TranscriptProjectState> CreateAsync(
        CreateTranscriptProjectRequest request,
        CancellationToken cancellationToken)
    {
        CreateProjectFromMediaResult createResult = await projectMediaIngestService.CreateAsync(
            new CreateProjectFromMediaRequest(request.ProjectName, request.SourceMediaPath),
            cancellationToken).ConfigureAwait(false);

        await GenerateTranscriptAsync(
            createResult.Project.Id,
            createResult.MediaAsset,
            createResult.AudioArtifact,
            cancellationToken).ConfigureAwait(false);

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> OpenAsync(CancellationToken cancellationToken)
    {
        OpenProjectResult openResult = await projectMediaIngestService.OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision? currentRevision = await transcriptRepository.GetCurrentRevisionAsync(
            openResult.Project.Id,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TranscriptSegment> segments = currentRevision is null
            ? []
            : await transcriptRepository.GetSegmentsAsync(currentRevision.Id, cancellationToken).ConfigureAwait(false);

        string? translationTargetLanguage = GetTranslationTargetLanguage(openResult.TranscriptLanguage);
        TranslationRevision? currentTranslationRevision = translationTargetLanguage is null
            ? null
            : await translationRepository.GetCurrentRevisionAsync(
                openResult.Project.Id,
                translationTargetLanguage,
                cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TranslatedSegment> translatedSegments = currentTranslationRevision is null
            ? []
            : await translationRepository.GetSegmentsAsync(currentTranslationRevision.Id, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<StageRunRecord> stageRuns = await stageRunStore.ListByProjectAsync(
            openResult.Project.Id,
            cancellationToken).ConfigureAwait(false);

        bool isTranslationStale = currentRevision is not null &&
                                  currentTranslationRevision is not null &&
                                  currentTranslationRevision.SourceTranscriptRevisionId != currentRevision.Id;

        return new TranscriptProjectState(
            openResult,
            currentRevision,
            segments,
            currentTranslationRevision,
            translatedSegments,
            isTranslationStale,
            openResult.TranscriptLanguage,
            stageRuns,
            await ReadWaveformSummaryAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<TranscriptProjectState> RelocateSourceAsync(
        RelocateTranscriptSourceRequest request,
        CancellationToken cancellationToken)
    {
        await projectMediaIngestService.RelocateSourceAsync(
            new RelocateSourceMediaRequest(request.NewSourceMediaPath),
            cancellationToken).ConfigureAwait(false);

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> SaveEditsAsync(
        SaveTranscriptEditsRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Transcript edits were based on an out-of-date revision.");

        Dictionary<Guid, string> replacements = request.Segments.ToDictionary(
            segment => segment.SegmentId,
            segment => segment.Text,
            comparer: EqualityComparer<Guid>.Default);

        TranscriptSegment[] editedSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select((segment, index) => TranscriptSegment.Create(
                currentRevision.Id,
                index,
                segment.StartSeconds,
                segment.EndSeconds,
                replacements.TryGetValue(segment.Id, out string? replacement)
                    ? replacement
                    : segment.Text))
            .ToArray();

        return await SaveTranscriptRevisionAsync(currentState, editedSegments, "manual-edit", cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> SplitSegmentAsync(
        SplitTranscriptSegmentRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Segment split was based on an out-of-date transcript revision.");

        TranscriptSegment[] existingSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .ToArray();
        int targetIndex = Array.FindIndex(existingSegments, segment => segment.Id == request.SegmentId);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("The selected segment was not found in the current transcript revision.");
        }

        TranscriptSegment targetSegment = existingSegments[targetIndex];
        if (!double.IsFinite(request.SplitSeconds) ||
            request.SplitSeconds <= targetSegment.StartSeconds ||
            request.SplitSeconds >= targetSegment.EndSeconds)
        {
            throw new InvalidOperationException("Split time must fall inside the selected segment.");
        }

        (string leftText, string rightText) = SplitSegmentText(targetSegment.Text);
        var revisedSegments = new List<TranscriptSegment>(existingSegments.Length + 1);
        int revisedIndex = 0;
        foreach (TranscriptSegment segment in existingSegments)
        {
            if (segment.Id != targetSegment.Id)
            {
                revisedSegments.Add(TranscriptSegment.Create(
                    currentRevision.Id,
                    revisedIndex++,
                    segment.StartSeconds,
                    segment.EndSeconds,
                    segment.Text));
                continue;
            }

            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                segment.StartSeconds,
                request.SplitSeconds,
                leftText));
            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                request.SplitSeconds,
                segment.EndSeconds,
                rightText));
        }

        return await SaveTranscriptRevisionAsync(currentState, revisedSegments, "segment-split", cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> MergeSegmentsAsync(
        MergeTranscriptSegmentsRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Segment merge was based on an out-of-date transcript revision.");

        TranscriptSegment[] existingSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .ToArray();
        TranscriptSegment firstSegment = existingSegments.FirstOrDefault(segment => segment.Id == request.FirstSegmentId)
            ?? throw new InvalidOperationException("The first selected segment was not found in the current transcript revision.");
        TranscriptSegment secondSegment = existingSegments.FirstOrDefault(segment => segment.Id == request.SecondSegmentId)
            ?? throw new InvalidOperationException("The second selected segment was not found in the current transcript revision.");

        TranscriptSegment left = firstSegment.SegmentIndex <= secondSegment.SegmentIndex ? firstSegment : secondSegment;
        TranscriptSegment right = ReferenceEquals(left, firstSegment) ? secondSegment : firstSegment;
        if (right.SegmentIndex - left.SegmentIndex != 1)
        {
            throw new InvalidOperationException("Only adjacent transcript segments can be merged.");
        }

        var revisedSegments = new List<TranscriptSegment>(existingSegments.Length - 1);
        int revisedIndex = 0;
        foreach (TranscriptSegment segment in existingSegments)
        {
            if (segment.Id == left.Id)
            {
                revisedSegments.Add(TranscriptSegment.Create(
                    currentRevision.Id,
                    revisedIndex++,
                    left.StartSeconds,
                    right.EndSeconds,
                    MergeSegmentText(left.Text, right.Text)));
                continue;
            }

            if (segment.Id == right.Id)
            {
                continue;
            }

            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text));
        }

        return await SaveTranscriptRevisionAsync(currentState, revisedSegments, "segment-merge", cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> TrimSegmentAsync(
        TrimTranscriptSegmentRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Segment trim was based on an out-of-date transcript revision.");

        if (!double.IsFinite(request.StartSeconds) ||
            !double.IsFinite(request.EndSeconds) ||
            request.StartSeconds < 0 ||
            request.EndSeconds < request.StartSeconds)
        {
            throw new InvalidOperationException("Trim start and end times must be finite, non-negative, and ordered.");
        }

        TranscriptSegment[] existingSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .ToArray();
        int targetIndex = Array.FindIndex(existingSegments, segment => segment.Id == request.SegmentId);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("The selected segment was not found in the current transcript revision.");
        }

        TranscriptSegment targetSegment = existingSegments[targetIndex];
        double previousEnd = targetIndex == 0 ? 0d : existingSegments[targetIndex - 1].EndSeconds;
        double nextStart = targetIndex == existingSegments.Length - 1 ? double.PositiveInfinity : existingSegments[targetIndex + 1].StartSeconds;
        if (request.StartSeconds < previousEnd || request.EndSeconds > nextStart)
        {
            throw new InvalidOperationException("Trimmed segment timing would overlap an adjacent segment.");
        }

        var revisedSegments = new List<TranscriptSegment>(existingSegments.Length);
        int revisedIndex = 0;
        foreach (TranscriptSegment segment in existingSegments)
        {
            bool isTarget = segment.Id == targetSegment.Id;
            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                isTarget ? request.StartSeconds : segment.StartSeconds,
                isTarget ? request.EndSeconds : segment.EndSeconds,
                segment.Text));
        }

        return await SaveTranscriptRevisionAsync(currentState, revisedSegments, "segment-trim", cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> SetTranscriptLanguageAsync(
        SetTranscriptLanguageRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        string? transcriptLanguage = NormalizeTranscriptLanguageCode(request.TranscriptLanguage);
        if (string.Equals(currentState.TranscriptLanguage, transcriptLanguage, StringComparison.Ordinal))
        {
            return currentState;
        }

        ProjectManifest manifest = await ReadProjectManifestAsync(
            currentState.ProjectState.Project,
            currentState.TranscriptLanguage,
            cancellationToken).ConfigureAwait(false);
        await artifactStore.WriteJsonAsync(
            ProjectArtifactPaths.ManifestRelativePath,
            manifest.WithTranscriptLanguage(transcriptLanguage),
            cancellationToken).ConfigureAwait(false);

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> GenerateTranslationAsync(
        GenerateTranslationRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentTranscriptRevision = GetRequiredTranscriptRevision(currentState);

        string sourceLanguage = NormalizeRequiredTranscriptLanguageCode(request.SourceLanguage);
        string targetLanguage = NormalizeSupportedTargetLanguage(request.TargetLanguage, sourceLanguage);

        if (!string.Equals(currentState.TranscriptLanguage, sourceLanguage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Set the transcript language before starting translation.");
        }

        StageRunRecord translationStageRun = StageRunRecord.Start(
            currentState.ProjectState.Project.Id,
            "translation",
            DateTimeOffset.UtcNow);
        await stageRunStore.CreateAsync(translationStageRun, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TranslatedTextSegment> translatedTextSegments;
        try
        {
            translatedTextSegments = await translationEngine.TranslateAsync(
                new TranslationRequest(
                    sourceLanguage,
                    targetLanguage,
                    currentState.TranscriptSegments
                        .OrderBy(segment => segment.SegmentIndex)
                        .Select(segment => new TranslationInputSegment(
                            segment.SegmentIndex,
                            segment.StartSeconds,
                            segment.EndSeconds,
                            segment.Text))
                        .ToArray(),
                    CommercialSafeMode: true,
                    PreferredModelAlias: ResolvePreferredTranslationModelAlias(sourceLanguage, targetLanguage)),
                cancellationToken).ConfigureAwait(false);

            translationStageRun = ApplyRuntimeExecutionSummary(translationStageRun, translationEngine)
                .Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(translationStageRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageRunRecord failed = ApplyRuntimeExecutionSummary(translationStageRun, translationEngine)
                .Fail(DateTimeOffset.UtcNow, ex.Message);
            await stageRunStore.UpdateAsync(failed, cancellationToken).ConfigureAwait(false);
            throw;
        }

        int nextRevisionNumber = await translationRepository.GetNextRevisionNumberAsync(
            currentState.ProjectState.Project.Id,
            targetLanguage,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranslationRevision translationRevision = TranslationRevision.Create(
            currentState.ProjectState.Project.Id,
            translationStageRun.Id,
            currentTranscriptRevision.Id,
            targetLanguage,
            nextRevisionNumber,
            now);

        TranslatedSegment[] translatedSegments = translatedTextSegments
            .OrderBy(segment => segment.Index)
            .Select(segment => TranslatedSegment.Create(
                translationRevision.Id,
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text))
            .ToArray();

        await translationRepository.SaveRevisionAsync(
            translationRevision,
            translatedSegments,
            cancellationToken).ConfigureAwait(false);
        await WriteTranslationArtifactAsync(
            currentState.ProjectState.Project.Id,
            GetRequiredMediaAsset(currentState),
            translationRevision,
            translatedSegments,
            stageRunId: translationStageRun.Id,
            provenance: "generated-translation",
            cancellationToken).ConfigureAwait(false);

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> SaveTranslationEditsAsync(
        SaveTranslationEditsRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranslationRevision currentTranslationRevision = currentState.CurrentTranslationRevision
            ?? throw new InvalidOperationException("The project does not contain a translation revision.");

        if (currentTranslationRevision.Id != request.TranslationRevisionId)
        {
            throw new InvalidOperationException("Translation edits were based on an out-of-date revision.");
        }

        string targetLanguage = NormalizeSupportedTargetLanguage(
            request.TargetLanguage,
            currentState.TranscriptLanguage ?? throw new InvalidOperationException("Set the transcript language before saving translation edits."));
        if (!string.Equals(currentTranslationRevision.TargetLanguage, targetLanguage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Translation edits were based on a different target language.");
        }

        Dictionary<int, string> replacements = request.Segments.ToDictionary(
            segment => segment.SegmentIndex,
            segment => segment.Text);
        int nextRevisionNumber = await translationRepository.GetNextRevisionNumberAsync(
            currentState.ProjectState.Project.Id,
            targetLanguage,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranslationRevision editedRevision = TranslationRevision.Create(
            currentState.ProjectState.Project.Id,
            stageRunId: null,
            currentTranslationRevision.SourceTranscriptRevisionId,
            targetLanguage,
            nextRevisionNumber,
            now);

        TranslatedSegment[] editedSegments = currentState.TranslatedSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select(segment => TranslatedSegment.Create(
                editedRevision.Id,
                segment.SegmentIndex,
                segment.StartSeconds,
                segment.EndSeconds,
                replacements.TryGetValue(segment.SegmentIndex, out string? replacement)
                    ? replacement
                    : segment.Text))
            .ToArray();

        await translationRepository.SaveRevisionAsync(editedRevision, editedSegments, cancellationToken).ConfigureAwait(false);
        await WriteTranslationArtifactAsync(
            currentState.ProjectState.Project.Id,
            GetRequiredMediaAsset(currentState),
            editedRevision,
            editedSegments,
            stageRunId: null,
            provenance: "manual-edit",
            cancellationToken).ConfigureAwait(false);

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranscriptProjectState> SaveTranscriptRevisionAsync(
        TranscriptProjectState currentState,
        IReadOnlyList<TranscriptSegment> segments,
        string provenance,
        CancellationToken cancellationToken)
    {
        int nextRevisionNumber = await transcriptRepository.GetNextRevisionNumberAsync(
            currentState.ProjectState.Project.Id,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranscriptRevision editedRevision = TranscriptRevision.Create(
            currentState.ProjectState.Project.Id,
            stageRunId: null,
            nextRevisionNumber,
            now);

        TranscriptSegment[] revisedSegments = segments
            .OrderBy(segment => segment.SegmentIndex)
            .Select((segment, index) => TranscriptSegment.Create(
                editedRevision.Id,
                index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text))
            .ToArray();

        await transcriptRepository.SaveRevisionAsync(editedRevision, revisedSegments, cancellationToken).ConfigureAwait(false);
        await WriteTranscriptArtifactAsync(
            currentState.ProjectState.Project.Id,
            GetRequiredMediaAsset(currentState),
            editedRevision,
            revisedSegments,
            stageRunId: null,
            provenance,
            cancellationToken).ConfigureAwait(false);

        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task GenerateTranscriptAsync(
        Guid projectId,
        MediaAsset mediaAsset,
        ProjectArtifact audioArtifact,
        CancellationToken cancellationToken)
    {
        double durationSeconds = audioArtifact.DurationSeconds ?? mediaAsset.DurationSeconds;
        string normalizedAudioPath = artifactStore.GetPath(ProjectArtifactPaths.NormalizedAudioRelativePath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StageRunRecord vadStageRun = StageRunRecord.Start(projectId, "vad", now);
        await stageRunStore.CreateAsync(vadStageRun, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SpeechRegion> regions;
        try
        {
            regions = await speechRegionDetector.DetectAsync(
                normalizedAudioPath,
                durationSeconds,
                cancellationToken).ConfigureAwait(false);

            vadStageRun = ApplyRuntimeExecutionSummary(vadStageRun, speechRegionDetector)
                .Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(vadStageRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageRunRecord failed = ApplyRuntimeExecutionSummary(vadStageRun, speechRegionDetector)
                .Fail(DateTimeOffset.UtcNow, ex.Message);
            await stageRunStore.UpdateAsync(failed, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await WriteSpeechRegionsArtifactAsync(projectId, mediaAsset, regions, vadStageRun, cancellationToken).ConfigureAwait(false);

        StageRunRecord asrStageRun = StageRunRecord.Start(projectId, "asr", DateTimeOffset.UtcNow);
        await stageRunStore.CreateAsync(asrStageRun, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RecognizedTranscriptSegment> recognizedSegments;
        try
        {
            recognizedSegments = await transcriptionEngine.TranscribeAsync(
                normalizedAudioPath,
                regions,
                cancellationToken).ConfigureAwait(false);

            asrStageRun = ApplyRuntimeExecutionSummary(asrStageRun, transcriptionEngine)
                .Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(asrStageRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageRunRecord failed = ApplyRuntimeExecutionSummary(asrStageRun, transcriptionEngine)
                .Fail(DateTimeOffset.UtcNow, ex.Message);
            await stageRunStore.UpdateAsync(failed, cancellationToken).ConfigureAwait(false);
            throw;
        }

        int revisionNumber = await transcriptRepository.GetNextRevisionNumberAsync(projectId, cancellationToken).ConfigureAwait(false);
        TranscriptRevision revision = TranscriptRevision.Create(projectId, asrStageRun.Id, revisionNumber, DateTimeOffset.UtcNow);
        TranscriptSegment[] segments = recognizedSegments
            .OrderBy(segment => segment.Index)
            .Select(segment => TranscriptSegment.Create(
                revision.Id,
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text))
            .ToArray();

        await transcriptRepository.SaveRevisionAsync(revision, segments, cancellationToken).ConfigureAwait(false);
        await WriteTranscriptArtifactAsync(
            projectId,
            mediaAsset,
            revision,
            segments,
            asrStageRun.Id,
            "generated-asr",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSpeechRegionsArtifactAsync(
        Guid projectId,
        MediaAsset mediaAsset,
        IReadOnlyList<SpeechRegion> regions,
        StageRunRecord stageRun,
        CancellationToken cancellationToken)
    {
        string relativePath = ProjectArtifactPaths.GetSpeechRegionsRelativePath();
        await artifactStore.WriteJsonAsync(
            relativePath,
            new SpeechRegionsArtifactDocument(stageRun.Id, regions, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        FileFingerprint fingerprint = await fileFingerprintService.ComputeAsync(
            artifactStore.GetPath(relativePath),
            cancellationToken).ConfigureAwait(false);

        var artifact = new ProjectArtifact(
            Guid.NewGuid(),
            projectId,
            mediaAsset.Id,
            ArtifactKind.SpeechRegions,
            relativePath,
            fingerprint.Sha256,
            fingerprint.SizeBytes,
            DurationSeconds: null,
            SampleRate: null,
            ChannelCount: null,
            DateTimeOffset.UtcNow,
            StageRunId: stageRun.Id,
            Provenance: "generated-vad");

        await mediaAssetRepository.SaveArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteTranscriptArtifactAsync(
        Guid projectId,
        MediaAsset mediaAsset,
        TranscriptRevision revision,
        IReadOnlyList<TranscriptSegment> segments,
        Guid? stageRunId,
        string provenance,
        CancellationToken cancellationToken)
    {
        string relativePath = ProjectArtifactPaths.GetTranscriptRevisionRelativePath(revision.RevisionNumber);
        await artifactStore.WriteJsonAsync(
            relativePath,
            TranscriptRevisionArtifactDocument.From(revision, segments, provenance),
            cancellationToken).ConfigureAwait(false);

        FileFingerprint fingerprint = await fileFingerprintService.ComputeAsync(
            artifactStore.GetPath(relativePath),
            cancellationToken).ConfigureAwait(false);

        var artifact = new ProjectArtifact(
            Guid.NewGuid(),
            projectId,
            mediaAsset.Id,
            ArtifactKind.TranscriptRevision,
            relativePath,
            fingerprint.Sha256,
            fingerprint.SizeBytes,
            DurationSeconds: null,
            SampleRate: null,
            ChannelCount: null,
            DateTimeOffset.UtcNow,
            StageRunId: stageRunId,
            Provenance: provenance);

        await mediaAssetRepository.SaveArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteTranslationArtifactAsync(
        Guid projectId,
        MediaAsset mediaAsset,
        TranslationRevision revision,
        IReadOnlyList<TranslatedSegment> segments,
        Guid? stageRunId,
        string provenance,
        CancellationToken cancellationToken)
    {
        string relativePath = ProjectArtifactPaths.GetTranslationRevisionRelativePath(
            revision.TargetLanguage,
            revision.RevisionNumber);
        await artifactStore.WriteJsonAsync(
            relativePath,
            TranslationRevisionArtifactDocument.From(revision, segments, provenance),
            cancellationToken).ConfigureAwait(false);

        FileFingerprint fingerprint = await fileFingerprintService.ComputeAsync(
            artifactStore.GetPath(relativePath),
            cancellationToken).ConfigureAwait(false);

        var artifact = new ProjectArtifact(
            Guid.NewGuid(),
            projectId,
            mediaAsset.Id,
            ArtifactKind.TranslationRevision,
            relativePath,
            fingerprint.Sha256,
            fingerprint.SizeBytes,
            DurationSeconds: null,
            SampleRate: null,
            ChannelCount: null,
            DateTimeOffset.UtcNow,
            StageRunId: stageRunId,
            Provenance: provenance);

        await mediaAssetRepository.SaveArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectManifest> ReadProjectManifestAsync(
        BabelProject project,
        string? transcriptLanguage,
        CancellationToken cancellationToken)
    {
        ProjectManifest? manifest = await artifactStore.ReadJsonAsync<ProjectManifest>(
            ProjectArtifactPaths.ManifestRelativePath,
            cancellationToken).ConfigureAwait(false);
        return manifest ?? ProjectManifest.FromProject(project, transcriptLanguage);
    }

    private Task<WaveformSummary?> ReadWaveformSummaryAsync(CancellationToken cancellationToken)
    {
        return artifactStore.ReadJsonAsync<WaveformSummary>(
            ProjectArtifactPaths.WaveformSummaryRelativePath,
            cancellationToken);
    }

    private static TranscriptRevision GetRequiredTranscriptRevision(TranscriptProjectState state) =>
        state.CurrentTranscriptRevision
            ?? throw new InvalidOperationException("The project does not contain a transcript revision.");

    private static void EnsureRevisionMatches(TranscriptRevision revision, Guid expectedRevisionId, string message)
    {
        if (revision.Id != expectedRevisionId)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static (string Left, string Right) SplitSegmentText(string text)
    {
        string trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ("[split]", "[split]");
        }

        string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            return (trimmed, trimmed);
        }

        int midpoint = words.Length / 2;
        return (
            string.Join(' ', words.Take(midpoint)),
            string.Join(' ', words.Skip(midpoint)));
    }

    private static string MergeSegmentText(string first, string second)
    {
        string left = first.Trim();
        string right = second.Trim();
        return string.IsNullOrWhiteSpace(left)
            ? right
            : string.IsNullOrWhiteSpace(right)
                ? left
                : $"{left} {right}";
    }

    private static MediaAsset GetRequiredMediaAsset(TranscriptProjectState state) =>
        state.ProjectState.MediaAsset
            ?? throw new InvalidOperationException("The project does not contain a primary media asset.");

    private static StageRunRecord ApplyRuntimeExecutionSummary(StageRunRecord stageRun, object stageEngine)
    {
        if (stageEngine is not IStageRuntimeExecutionReporter reporter ||
            reporter.LastExecutionSummary is null)
        {
            return stageRun;
        }

        StageRuntimeExecutionSummary summary = reporter.LastExecutionSummary;
        return stageRun.WithRuntimeInfo(
            summary.RequestedProvider,
            summary.SelectedProvider,
            summary.ModelId,
            summary.ModelAlias,
            summary.ModelVariant,
            summary.BootstrapDetail);
    }

    private static string NormalizeRequiredTranscriptLanguageCode(string languageCode)
    {
        string normalized = NormalizeTranscriptLanguageCode(languageCode)
            ?? throw new InvalidOperationException("Transcript language is required.");
        return normalized;
    }

    private static string NormalizeSupportedTargetLanguage(string targetLanguage, string sourceLanguage)
    {
        string normalizedSource = NormalizeRequiredTranscriptLanguageCode(sourceLanguage);
        string normalizedTarget = NormalizeRequiredTranscriptLanguageCode(targetLanguage);
        string? expectedTarget = GetTranslationTargetLanguage(normalizedSource);
        if (!string.Equals(normalizedTarget, expectedTarget, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Milestone 7 only supports direct English <-> Spanish translation. Requested pair was {normalizedSource} -> {normalizedTarget}.");
        }

        return normalizedTarget;
    }

    private static string? NormalizeTranscriptLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        string normalized = languageCode.Trim().ToLowerInvariant();
        return normalized switch
        {
            EnglishLanguageCode => normalized,
            SpanishLanguageCode => normalized,
            _ => throw new InvalidOperationException("Milestone 7 only supports English or Spanish transcript language selection.")
        };
    }

    private static string? GetTranslationTargetLanguage(string? sourceLanguage)
    {
        string? normalizedSource = NormalizeTranscriptLanguageCode(sourceLanguage);
        return normalizedSource switch
        {
            EnglishLanguageCode => SpanishLanguageCode,
            SpanishLanguageCode => EnglishLanguageCode,
            _ => null
        };
    }

    private static string ResolvePreferredTranslationModelAlias(string sourceLanguage, string targetLanguage)
    {
        return (NormalizeRequiredTranscriptLanguageCode(sourceLanguage), NormalizeRequiredTranscriptLanguageCode(targetLanguage)) switch
        {
            (EnglishLanguageCode, SpanishLanguageCode) => "opus-en-es",
            (SpanishLanguageCode, EnglishLanguageCode) => "opus-es-en",
            _ => throw new InvalidOperationException(
                $"Milestone 7 only supports direct English <-> Spanish translation. Requested pair was {sourceLanguage} -> {targetLanguage}.")
        };
    }

    private sealed record SpeechRegionsArtifactDocument(
        Guid StageRunId,
        IReadOnlyList<SpeechRegion> Regions,
        DateTimeOffset GeneratedAtUtc);

    private sealed record TranscriptRevisionArtifactDocument(
        Guid RevisionId,
        Guid? StageRunId,
        int RevisionNumber,
        string Provenance,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<TranscriptSegmentArtifactDocument> Segments)
    {
        public static TranscriptRevisionArtifactDocument From(
            TranscriptRevision revision,
            IReadOnlyList<TranscriptSegment> segments,
            string provenance) =>
            new(
                revision.Id,
                revision.StageRunId,
                revision.RevisionNumber,
                provenance,
                revision.CreatedAtUtc,
                segments
                    .OrderBy(segment => segment.SegmentIndex)
                    .Select(segment => new TranscriptSegmentArtifactDocument(
                        segment.SegmentIndex,
                        segment.StartSeconds,
                        segment.EndSeconds,
                        segment.Text))
                    .ToArray());
    }

    private sealed record TranslationRevisionArtifactDocument(
        Guid RevisionId,
        Guid? StageRunId,
        Guid SourceTranscriptRevisionId,
        string TargetLanguage,
        int RevisionNumber,
        string Provenance,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<TranslatedSegmentArtifactDocument> Segments)
    {
        public static TranslationRevisionArtifactDocument From(
            TranslationRevision revision,
            IReadOnlyList<TranslatedSegment> segments,
            string provenance) =>
            new(
                revision.Id,
                revision.StageRunId,
                revision.SourceTranscriptRevisionId,
                revision.TargetLanguage,
                revision.RevisionNumber,
                provenance,
                revision.CreatedAtUtc,
                segments
                    .OrderBy(segment => segment.SegmentIndex)
                    .Select(segment => new TranslatedSegmentArtifactDocument(
                        segment.SegmentIndex,
                        segment.StartSeconds,
                        segment.EndSeconds,
                        segment.Text))
                    .ToArray());
    }

    private sealed record TranscriptSegmentArtifactDocument(
        int SegmentIndex,
        double StartSeconds,
        double EndSeconds,
        string Text);

    private sealed record TranslatedSegmentArtifactDocument(
        int SegmentIndex,
        double StartSeconds,
        double EndSeconds,
        string Text);
}
