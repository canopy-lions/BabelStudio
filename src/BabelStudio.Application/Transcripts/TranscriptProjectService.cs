using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Speakers;
using BabelStudio.Domain.Transcript;
using BabelStudio.Domain.Translation;
using BabelStudio.Domain.Tts;

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
    private readonly ISpeakerRepository speakerRepository;
    private readonly IArtifactStore artifactStore;
    private readonly IAudioClipExtractor audioClipExtractor;
    private readonly IFileFingerprintService fileFingerprintService;
    private readonly ISpeechRegionDetector speechRegionDetector;
    private readonly ISpeakerDiarizationEngine diarizationEngine;
    private readonly IAudioTranscriptionEngine transcriptionEngine;
    private readonly ITranslationLanguageRouter translationLanguageRouter;
    private readonly ITranslationEngine translationEngine;
    private readonly IVoiceAssignmentRepository voiceAssignmentRepository;
    private readonly ITtsTakeRepository ttsTakeRepository;
    private readonly ITtsEngine ttsEngine;
    private readonly IVoiceCatalog voiceCatalog;
    private readonly RenameSpeakerHandler renameSpeakerHandler;
    private readonly MergeSpeakersHandler mergeSpeakersHandler;
    private readonly StartTtsStageHandler startTtsStageHandler;

    public TranscriptProjectService(
        ProjectMediaIngestService projectMediaIngestService,
        ITranscriptRepository transcriptRepository,
        ITranslationRepository translationRepository,
        IProjectStageRunStore stageRunStore,
        IMediaAssetRepository mediaAssetRepository,
        ISpeakerRepository speakerRepository,
        IArtifactStore artifactStore,
        IAudioClipExtractor audioClipExtractor,
        IFileFingerprintService fileFingerprintService,
        ISpeechRegionDetector speechRegionDetector,
        ISpeakerDiarizationEngine diarizationEngine,
        IAudioTranscriptionEngine transcriptionEngine,
        ITranslationLanguageRouter translationLanguageRouter,
        ITranslationEngine translationEngine,
        IVoiceAssignmentRepository voiceAssignmentRepository,
        ITtsTakeRepository ttsTakeRepository,
        ITtsEngine ttsEngine,
        IVoiceCatalog voiceCatalog)
    {
        this.projectMediaIngestService = projectMediaIngestService;
        this.transcriptRepository = transcriptRepository;
        this.translationRepository = translationRepository;
        this.stageRunStore = stageRunStore;
        this.mediaAssetRepository = mediaAssetRepository;
        this.speakerRepository = speakerRepository;
        this.artifactStore = artifactStore;
        this.audioClipExtractor = audioClipExtractor;
        this.fileFingerprintService = fileFingerprintService;
        this.speechRegionDetector = speechRegionDetector;
        this.diarizationEngine = diarizationEngine;
        this.transcriptionEngine = transcriptionEngine;
        this.translationLanguageRouter = translationLanguageRouter;
        this.translationEngine = translationEngine;
        this.voiceAssignmentRepository = voiceAssignmentRepository;
        this.ttsTakeRepository = ttsTakeRepository;
        this.ttsEngine = ttsEngine;
        this.voiceCatalog = voiceCatalog;
        renameSpeakerHandler = new RenameSpeakerHandler(speakerRepository);
        mergeSpeakersHandler = new MergeSpeakersHandler(transcriptRepository);
        startTtsStageHandler = new StartTtsStageHandler(
            ttsEngine,
            voiceCatalog,
            artifactStore,
            fileFingerprintService,
            mediaAssetRepository,
            ttsTakeRepository,
            stageRunStore);
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
            request.EnableSpeakerDiarization,
            request.CommercialSafeMode,
            cancellationToken).ConfigureAwait(false);

        return await OpenAsyncCore(requestedTranslationTargetLanguage: null, cancellationToken).ConfigureAwait(false);
    }

    public Task<TranscriptProjectState> OpenAsync(CancellationToken cancellationToken) =>
        OpenAsyncCore(requestedTranslationTargetLanguage: null, cancellationToken);

    public Task<TranscriptProjectState> SelectTranslationTargetAsync(
        SetTranslationTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OpenAsyncCore(request.TargetLanguage, cancellationToken);
    }

    private async Task<TranscriptProjectState> OpenAsyncCore(
        string? requestedTranslationTargetLanguage,
        CancellationToken cancellationToken)
    {
        OpenProjectResult openResult = await projectMediaIngestService.OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision? currentRevision = await transcriptRepository.GetCurrentRevisionAsync(
            openResult.Project.Id,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProjectSpeaker> speakers = await LoadOrCreateSpeakersAsync(
            openResult.Project.Id,
            currentRevision,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TranscriptSegment> storedSegments = currentRevision is null
            ? []
            : await transcriptRepository.GetSegmentsAsync(currentRevision.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<TranscriptSegment> segments = ApplySingleSpeakerDefaultAssignments(storedSegments, speakers);
        IReadOnlyList<SpeakerTurn> speakerTurns = await speakerRepository.ListTurnsAsync(
            openResult.Project.Id,
            cancellationToken).ConfigureAwait(false);

        string? normalizedTranscriptLanguage = NormalizeTranscriptLanguageCode(openResult.TranscriptLanguage);
        IReadOnlyList<TranslationTargetLanguageOption> supportedTargetLanguages = normalizedTranscriptLanguage is null
            ? []
            : await translationLanguageRouter.GetSupportedTargetLanguagesAsync(
                normalizedTranscriptLanguage,
                commercialSafeMode: false,
                cancellationToken).ConfigureAwait(false);
        string? selectedTranslationTargetLanguage = ResolveSelectedTranslationTargetLanguage(
            normalizedTranscriptLanguage,
            requestedTranslationTargetLanguage,
            supportedTargetLanguages);

        TranslationRevision? currentTranslationRevision = selectedTranslationTargetLanguage is null
            ? null
            : await translationRepository.GetCurrentRevisionAsync(
                openResult.Project.Id,
                selectedTranslationTargetLanguage,
                cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TranslatedSegment> translatedSegments = currentTranslationRevision is null
            ? []
            : await translationRepository.GetSegmentsAsync(currentTranslationRevision.Id, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<StageRunRecord> stageRuns = await stageRunStore.ListByProjectAsync(
            openResult.Project.Id,
            cancellationToken).ConfigureAwait(false);

        IReadOnlySet<int> staleTranslatedSegmentIndices = BuildStaleTranslatedSegmentIndices(
            currentRevision,
            segments,
            currentTranslationRevision,
            translatedSegments);
        bool isTranslationStale = staleTranslatedSegmentIndices.Count > 0;
        IReadOnlyList<VoiceAssignment> voiceAssignments = await voiceAssignmentRepository
            .GetAllAsync(openResult.Project.Id, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<VoiceCatalogEntry> availableVoices = voiceCatalog.GetVoices();
        IReadOnlyList<TtsTake> ttsTakes = await ttsTakeRepository
            .GetByProjectAsync(openResult.Project.Id, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<TtsSegmentState> ttsSegmentStates = BuildTtsSegmentStates(
            translatedSegments,
            ttsTakes,
            openResult.Artifacts);
        IReadOnlyList<VoiceAssignmentWarning> voiceAssignmentWarnings = BuildVoiceAssignmentWarnings(
            voiceAssignments,
            availableVoices,
            selectedTranslationTargetLanguage);

        return new TranscriptProjectState(
            openResult,
            currentRevision,
            segments,
            speakers,
            speakerTurns,
            currentTranslationRevision,
            translatedSegments,
            isTranslationStale,
            openResult.TranscriptLanguage,
            stageRuns,
            supportedTargetLanguages,
            selectedTranslationTargetLanguage,
            staleTranslatedSegmentIndices,
            await ReadWaveformSummaryAsync(cancellationToken).ConfigureAwait(false),
            availableVoices,
            voiceAssignments,
            ttsTakes,
            ttsSegmentStates,
            voiceAssignmentWarnings);
    }

    public async Task<TranscriptProjectState> RelocateSourceAsync(
        RelocateTranscriptSourceRequest request,
        CancellationToken cancellationToken)
    {
        await projectMediaIngestService.RelocateSourceAsync(
            new RelocateSourceMediaRequest(request.NewSourceMediaPath),
            cancellationToken).ConfigureAwait(false);

        return await OpenAsyncCore(requestedTranslationTargetLanguage: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> SaveEditsAsync(
        SaveTranscriptEditsRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Transcript edits were based on an out-of-date revision.");

        Dictionary<Guid, EditedTranscriptSegment> replacements = request.Segments.ToDictionary(
            segment => segment.SegmentId,
            segment => segment,
            comparer: EqualityComparer<Guid>.Default);

        TranscriptSegment[] editedSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select((segment, index) => TranscriptSegment.Create(
                currentRevision.Id,
                index,
                segment.StartSeconds,
                segment.EndSeconds,
                replacements.TryGetValue(segment.Id, out EditedTranscriptSegment? replacement)
                    ? replacement.Text
                    : segment.Text,
                replacements.TryGetValue(segment.Id, out replacement)
                    ? replacement.SpeakerId
                    : segment.SpeakerId))
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
                    segment.Text,
                    segment.SpeakerId));
                continue;
            }

            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                segment.StartSeconds,
                request.SplitSeconds,
                leftText,
                segment.SpeakerId));
            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                request.SplitSeconds,
                segment.EndSeconds,
                rightText,
                segment.SpeakerId));
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
        Guid? mergedSpeakerId = left.SpeakerId == right.SpeakerId ? left.SpeakerId : null;
        foreach (TranscriptSegment segment in existingSegments)
        {
            if (segment.Id == left.Id)
            {
                revisedSegments.Add(TranscriptSegment.Create(
                    currentRevision.Id,
                    revisedIndex++,
                    left.StartSeconds,
                    right.EndSeconds,
                    MergeSegmentText(left.Text, right.Text),
                    mergedSpeakerId));
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
                segment.Text,
                segment.SpeakerId));
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
                segment.Text,
                segment.SpeakerId));
        }

        return await SaveTranscriptRevisionAsync(currentState, revisedSegments, "segment-trim", cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> DeleteSegmentAsync(
        DeleteTranscriptSegmentRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Segment delete was based on an out-of-date transcript revision.");

        TranscriptSegment[] existingSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .ToArray();
        int targetIndex = Array.FindIndex(existingSegments, segment => segment.Id == request.SegmentId);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("The selected segment was not found in the current transcript revision.");
        }

        if (existingSegments.Length == 1)
        {
            throw new InvalidOperationException("Cannot delete the only remaining segment.");
        }

        var revisedSegments = new List<TranscriptSegment>(existingSegments.Length - 1);
        int revisedIndex = 0;
        foreach (TranscriptSegment segment in existingSegments)
        {
            if (segment.Id == request.SegmentId)
            {
                continue;
            }

            revisedSegments.Add(TranscriptSegment.Create(
                currentRevision.Id,
                revisedIndex++,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text,
                segment.SpeakerId));
        }

        return await SaveTranscriptRevisionAsync(currentState, revisedSegments, "segment-delete", cancellationToken).ConfigureAwait(false);
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

        return await OpenAsyncCore(requestedTranslationTargetLanguage: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> GenerateTranslationAsync(
        GenerateTranslationRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentTranscriptRevision = GetRequiredTranscriptRevision(currentState);

        string sourceLanguage = NormalizeRequiredTranscriptLanguageCode(request.SourceLanguage);
        string targetLanguage = NormalizeTranslationTargetLanguageCode(request.TargetLanguage);

        if (!string.Equals(currentState.TranscriptLanguage, sourceLanguage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Set the transcript language before starting translation.");
        }

        TranslationRouteSelection route = await translationLanguageRouter.ResolveRouteAsync(
            sourceLanguage,
            targetLanguage,
            request.CommercialSafeMode,
            cancellationToken).ConfigureAwait(false);
        if (!route.IsAvailable)
        {
            throw new InvalidOperationException(
                route.UnavailableReason ??
                $"Translation route {sourceLanguage} -> {targetLanguage} is not available.");
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
                    CommercialSafeMode: request.CommercialSafeMode),
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
        TranslationExecutionMetadata? executionMetadata = GetTranslationExecutionMetadata(translationEngine);
        TranslationRevision translationRevision = TranslationRevision.Create(
            currentState.ProjectState.Project.Id,
            translationStageRun.Id,
            currentTranscriptRevision.Id,
            targetLanguage,
            nextRevisionNumber,
            now,
            translationProvider: executionMetadata?.ProviderName ?? route.ProviderName,
            modelId: executionMetadata?.ModelId ?? route.ModelId,
            executionProvider: executionMetadata?.SelectedExecutionProvider);
        Dictionary<int, TranscriptSegment> sourceSegmentsByIndex = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .ToDictionary(segment => segment.SegmentIndex);

        TranslatedSegment[] translatedSegments = translatedTextSegments
            .OrderBy(segment => segment.Index)
            .Select(segment => TranslatedSegment.Create(
                translationRevision.Id,
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text,
                sourceSegmentsByIndex.TryGetValue(segment.Index, out TranscriptSegment? sourceSegment)
                    ? ComputeSourceSegmentHash(sourceSegment)
                    : null))
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

        return await OpenAsyncCore(targetLanguage, cancellationToken).ConfigureAwait(false);
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

        string targetLanguage = NormalizeTranslationTargetLanguageCode(
            request.TargetLanguage);
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
            now,
            currentTranslationRevision.TranslationProvider,
            currentTranslationRevision.ModelId,
            currentTranslationRevision.ExecutionProvider);
        Dictionary<int, string?> sourceHashesByIndex = currentState.TranslatedSegments
            .ToDictionary(segment => segment.SegmentIndex, segment => segment.SourceSegmentHash);
        HashSet<int> changedSegmentIndices = currentState.TranslatedSegments
            .Where(segment => replacements.TryGetValue(segment.SegmentIndex, out string? replacement) &&
                              !string.Equals(segment.Text, replacement, StringComparison.Ordinal))
            .Select(segment => segment.SegmentIndex)
            .ToHashSet();

        TranslatedSegment[] editedSegments = currentState.TranslatedSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select(segment => TranslatedSegment.Create(
                editedRevision.Id,
                segment.SegmentIndex,
                segment.StartSeconds,
                segment.EndSeconds,
                replacements.TryGetValue(segment.SegmentIndex, out string? replacement)
                    ? replacement
                    : segment.Text,
                sourceHashesByIndex.TryGetValue(segment.SegmentIndex, out string? sourceSegmentHash)
                    ? sourceSegmentHash
                    : null))
            .ToArray();

        await translationRepository.SaveRevisionAsync(editedRevision, editedSegments, cancellationToken).ConfigureAwait(false);
        if (changedSegmentIndices.Count > 0)
        {
            await ttsTakeRepository.MarkBySegmentIndicesStaleAsync(
                currentState.ProjectState.Project.Id,
                changedSegmentIndices,
                cancellationToken).ConfigureAwait(false);
        }

        await WriteTranslationArtifactAsync(
            currentState.ProjectState.Project.Id,
            GetRequiredMediaAsset(currentState),
            editedRevision,
            editedSegments,
            stageRunId: null,
            provenance: "manual-edit",
            cancellationToken).ConfigureAwait(false);

        return await OpenAsyncCore(targetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> RenameSpeakerAsync(
        RenameSpeakerRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await renameSpeakerHandler.HandleAsync(
            currentState.ProjectState.Project.Id,
            request.SpeakerId,
            request.DisplayName,
            cancellationToken).ConfigureAwait(false);
        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> MergeSpeakersAsync(
        MergeSpeakersRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await mergeSpeakersHandler.HandleAsync(
            currentState.ProjectState.Project.Id,
            request.SourceSpeakerId,
            request.TargetSpeakerId,
            cancellationToken).ConfigureAwait(false);
        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> AssignVoiceToSpeakerAsync(
        AssignVoiceToSpeakerRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!currentState.Speakers.Any(speaker => speaker.Id == request.SpeakerId))
        {
            throw new InvalidOperationException("The selected speaker was not found.");
        }

        if (!voiceCatalog.TryGetVoice(request.VoiceId, out VoiceCatalogEntry? voice))
        {
            throw new InvalidOperationException($"Voicepack '{request.VoiceId}' is not available.");
        }

        VoiceAssignment? existing = await voiceAssignmentRepository.GetAsync(
            currentState.ProjectState.Project.Id,
            request.SpeakerId,
            cancellationToken).ConfigureAwait(false);
        string voiceModelId = string.IsNullOrWhiteSpace(request.VoiceModelId)
            ? "kokoro-onnx"
            : request.VoiceModelId.Trim();
        VoiceAssignment assignment = existing is null
            ? VoiceAssignment.Create(
                currentState.ProjectState.Project.Id,
                request.SpeakerId,
                voiceModelId,
                voice.VoiceId)
            : existing.AssignVoice(voiceModelId, voice.VoiceId);
        bool voiceChanged = existing is not null &&
                            (!string.Equals(existing.VoiceModelId, assignment.VoiceModelId, StringComparison.Ordinal) ||
                             !string.Equals(existing.VoiceVariant, assignment.VoiceVariant, StringComparison.Ordinal));

        await voiceAssignmentRepository.SaveAsync(assignment, cancellationToken).ConfigureAwait(false);
        if (voiceChanged)
        {
            await ttsTakeRepository.MarkByVoiceAssignmentStaleAsync(
                currentState.ProjectState.Project.Id,
                assignment.Id,
                cancellationToken).ConfigureAwait(false);
        }

        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> GenerateTtsForSpeakerAsync(
        GenerateTtsForSpeakerRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await RunTtsForSpeakerAsync(currentState, request.SpeakerId, segmentIndices: null, cancellationToken)
            .ConfigureAwait(false);
        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> RegenerateStaleTtsForSpeakerAsync(
        RegenerateStaleTtsForSpeakerRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        HashSet<int> speakerSegmentIndices = currentState.TranscriptSegments
            .Where(segment => segment.SpeakerId == request.SpeakerId)
            .Select(segment => segment.SegmentIndex)
            .ToHashSet();
        HashSet<int> staleSegmentIndices = currentState.TtsSegmentStates
            .Where(state => state.IsStale && speakerSegmentIndices.Contains(state.SegmentIndex))
            .Select(state => state.SegmentIndex)
            .ToHashSet();
        if (staleSegmentIndices.Count == 0)
        {
            return currentState;
        }

        await RunTtsForSpeakerAsync(currentState, request.SpeakerId, staleSegmentIndices, cancellationToken)
            .ConfigureAwait(false);
        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> AssignSpeakerToSegmentAsync(
        AssignSpeakerToSegmentRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = GetRequiredTranscriptRevision(currentState);
        EnsureRevisionMatches(currentRevision, request.TranscriptRevisionId, "Speaker assignment was based on an out-of-date transcript revision.");

        TranscriptSegment[] revisedSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select((segment, index) => TranscriptSegment.Create(
                currentRevision.Id,
                index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text,
                segment.Id == request.SegmentId
                    ? request.SpeakerId
                    : segment.SpeakerId))
            .ToArray();

        return await SaveTranscriptRevisionAsync(
            currentState,
            revisedSegments,
            "speaker-assignment",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> SplitSpeakerTurnAsync(
        SplitSpeakerTurnRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await speakerRepository.SplitTurnAsync(
            currentState.ProjectState.Project.Id,
            request.SpeakerTurnId,
            request.SplitSeconds,
            cancellationToken).ConfigureAwait(false);
        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptProjectState> ExtractReferenceClipAsync(
        ExtractReferenceClipRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        MediaAsset mediaAsset = GetRequiredMediaAsset(currentState);
        ClipRange clipRange = ResolveReferenceClipRange(currentState, request);
        string sourceWavePath = artifactStore.GetPath(ProjectArtifactPaths.NormalizedAudioRelativePath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string relativePath = ProjectArtifactPaths.GetReferenceClipRelativePath(request.SpeakerId, now);
        ArtifactWriteHandle writeHandle = artifactStore.CreateWriteHandle(relativePath);
        bool committed = false;

        try
        {
            AudioClipExtractionResult extraction = await audioClipExtractor.ExtractAsync(
                sourceWavePath,
                clipRange.StartSeconds,
                clipRange.EndSeconds,
                writeHandle.TemporaryPath,
                cancellationToken).ConfigureAwait(false);
            await artifactStore.CommitAsync(writeHandle, cancellationToken).ConfigureAwait(false);

            FileFingerprint fingerprint = await fileFingerprintService.ComputeAsync(
                artifactStore.GetPath(relativePath),
                cancellationToken).ConfigureAwait(false);

            var artifact = new ProjectArtifact(
                Guid.NewGuid(),
                currentState.ProjectState.Project.Id,
                mediaAsset.Id,
                ArtifactKind.ReferenceClip,
                relativePath,
                fingerprint.Sha256,
                fingerprint.SizeBytes,
                extraction.DurationSeconds,
                extraction.SampleRate,
                extraction.ChannelCount,
                now,
                StageRunId: null,
                Provenance: $"speaker-reference:{request.SpeakerId:D}");
            await mediaAssetRepository.SaveArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);

            committed = true;
        }
        catch
        {
            if (File.Exists(artifactStore.GetPath(relativePath)))
            {
                File.Delete(artifactStore.GetPath(relativePath));
            }

            throw;
        }
        finally
        {
            if (!committed && File.Exists(writeHandle.TemporaryPath))
            {
                File.Delete(writeHandle.TemporaryPath);
            }
        }

        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
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
                segment.Text,
                segment.SpeakerId))
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

        return await OpenAsyncCore(currentState.SelectedTranslationTargetLanguage, cancellationToken).ConfigureAwait(false);
    }

    private async Task GenerateTranscriptAsync(
        Guid projectId,
        MediaAsset mediaAsset,
        ProjectArtifact audioArtifact,
        bool enableSpeakerDiarization,
        bool commercialSafeMode,
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
        SpeakerAssignmentResult speakerAssignment = enableSpeakerDiarization
            ? await CreateSpeakerAssignmentAsync(
                projectId,
                normalizedAudioPath,
                durationSeconds,
                regions,
                recognizedSegments,
                commercialSafeMode,
                cancellationToken).ConfigureAwait(false)
            : await CreateDefaultSpeakerAssignmentAsync(
                projectId,
                recognizedSegments,
                cancellationToken).ConfigureAwait(false);
        TranscriptSegment[] segments = recognizedSegments
            .OrderBy(segment => segment.Index)
            .Select(segment => TranscriptSegment.Create(
                revision.Id,
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Text,
                speakerAssignment.SegmentSpeakerIdsByIndex.TryGetValue(segment.Index, out Guid speakerId)
                    ? speakerId
                    : null))
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

    private async Task<IReadOnlyList<ProjectSpeaker>> LoadOrCreateSpeakersAsync(
        Guid projectId,
        TranscriptRevision? currentRevision,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProjectSpeaker> speakers = await speakerRepository.ListSpeakersAsync(
            projectId,
            cancellationToken).ConfigureAwait(false);
        if (speakers.Count > 0 || currentRevision is null)
        {
            return speakers;
        }

        ProjectSpeaker defaultSpeaker = await speakerRepository.EnsureDefaultSpeakerAsync(
            projectId,
            cancellationToken).ConfigureAwait(false);
        return [ defaultSpeaker ];
    }

    private static IReadOnlyList<TranscriptSegment> ApplySingleSpeakerDefaultAssignments(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<ProjectSpeaker> speakers)
    {
        if (segments.Count == 0 ||
            speakers.Count != 1 ||
            segments.All(segment => segment.SpeakerId is not null))
        {
            return segments;
        }

        Guid defaultSpeakerId = speakers[0].Id;
        return segments
            .OrderBy(segment => segment.SegmentIndex)
            .Select(segment => segment.SpeakerId is null
                ? segment with { SpeakerId = defaultSpeakerId }
                : segment)
            .ToArray();
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

    private async Task RunTtsForSpeakerAsync(
        TranscriptProjectState currentState,
        Guid speakerId,
        IReadOnlySet<int>? segmentIndices,
        CancellationToken cancellationToken)
    {
        if (!currentState.Speakers.Any(speaker => speaker.Id == speakerId))
        {
            throw new InvalidOperationException("The selected speaker was not found.");
        }

        TranslationRevision translationRevision = currentState.CurrentTranslationRevision
            ?? throw new InvalidOperationException("Generate or load a translation before starting TTS.");
        VoiceAssignment assignment = currentState.VoiceAssignments.FirstOrDefault(candidate => candidate.SpeakerId == speakerId)
            ?? throw new InvalidOperationException("Assign a Kokoro voicepack to the speaker before starting TTS.");

        await startTtsStageHandler.HandleAsync(
            new StartTtsStageRequest(
                currentState.ProjectState.Project.Id,
                GetRequiredMediaAsset(currentState),
                speakerId,
                translationRevision.TargetLanguage,
                assignment,
                currentState.TranscriptSegments,
                currentState.TranslatedSegments,
                segmentIndices),
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<TtsSegmentState> BuildTtsSegmentStates(
        IReadOnlyList<TranslatedSegment> translatedSegments,
        IReadOnlyList<TtsTake> ttsTakes,
        IReadOnlyList<ProjectArtifact> artifacts)
    {
        Dictionary<Guid, ProjectArtifact> artifactsById = artifacts
            .Where(artifact => artifact.Kind == ArtifactKind.TtsTake)
            .ToDictionary(artifact => artifact.Id);
        Dictionary<int, TtsTake> latestTakesBySegmentIndex = ttsTakes
            .GroupBy(take => take.SegmentIndex)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(take => take.CreatedAtUtc)
                    .First());

        return translatedSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select(segment =>
            {
                if (!latestTakesBySegmentIndex.TryGetValue(segment.SegmentIndex, out TtsTake? take))
                {
                    return new TtsSegmentState(
                        segment.SegmentIndex,
                        TakeId: null,
                        ArtifactRelativePath: null,
                        Status: null,
                        IsStale: false,
                        DurationSeconds: null,
                        DurationOverrunRatio: null,
                        HasDurationWarning: false,
                        WarningMessage: null);
                }

                bool textChanged = !string.IsNullOrWhiteSpace(take.TranslatedTextHash) &&
                                   !string.Equals(
                                       take.TranslatedTextHash,
                                       TtsTextHash.Compute(segment.SegmentIndex, segment.Text),
                                       StringComparison.Ordinal);
                bool isStale = take.IsStale || textChanged;
                ProjectArtifact? artifact = take.ArtifactId is Guid artifactId &&
                                            artifactsById.TryGetValue(artifactId, out ProjectArtifact? storedArtifact)
                    ? storedArtifact
                    : null;
                double? durationSeconds = take.DurationSamples is int durationSamples && take.SampleRate is int sampleRate && sampleRate > 0
                    ? (double)durationSamples / sampleRate
                    : artifact?.DurationSeconds;
                bool hasDurationWarning = StartTtsStageHandler.HasDurationWarning(take);
                return new TtsSegmentState(
                    segment.SegmentIndex,
                    take.Id,
                    artifact?.RelativePath,
                    take.Status,
                    isStale,
                    durationSeconds,
                    take.DurationOverrunRatio,
                    hasDurationWarning,
                    hasDurationWarning
                        ? "TTS duration exceeds the source segment by more than 10%."
                        : null);
            })
            .ToArray();
    }

    private static IReadOnlyList<VoiceAssignmentWarning> BuildVoiceAssignmentWarnings(
        IReadOnlyList<VoiceAssignment> voiceAssignments,
        IReadOnlyList<VoiceCatalogEntry> availableVoices,
        string? selectedTranslationTargetLanguage)
    {
        if (string.IsNullOrWhiteSpace(selectedTranslationTargetLanguage))
        {
            return [];
        }

        Dictionary<string, VoiceCatalogEntry> voicesById = availableVoices.ToDictionary(
            voice => voice.VoiceId,
            StringComparer.OrdinalIgnoreCase);
        string targetLanguage = selectedTranslationTargetLanguage.Trim().ToLowerInvariant();
        var warnings = new List<VoiceAssignmentWarning>();
        foreach (VoiceAssignment assignment in voiceAssignments)
        {
            string voiceId = string.IsNullOrWhiteSpace(assignment.VoiceVariant)
                ? assignment.VoiceModelId
                : assignment.VoiceVariant;
            if (!voicesById.TryGetValue(voiceId, out VoiceCatalogEntry? voice))
            {
                continue;
            }

            string voiceLanguage = NormalizeVoiceLanguageForComparison(voice.LanguageCode);
            if (!string.Equals(voiceLanguage, targetLanguage, StringComparison.Ordinal))
            {
                warnings.Add(new VoiceAssignmentWarning(
                    assignment.SpeakerId,
                    voice.VoiceId,
                    $"Voicepack language {voice.LanguageCode} does not match target {targetLanguage}."));
            }
        }

        return warnings;
    }

    private static string NormalizeVoiceLanguageForComparison(string languageCode)
    {
        string normalized = languageCode.Trim().ToLowerInvariant();
        int separatorIndex = normalized.IndexOfAny(['-', '_']);
        return separatorIndex <= 0 ? normalized : normalized[..separatorIndex];
    }

    private async Task<SpeakerAssignmentResult> CreateSpeakerAssignmentAsync(
        Guid projectId,
        string normalizedAudioPath,
        double durationSeconds,
        IReadOnlyList<SpeechRegion> regions,
        IReadOnlyList<RecognizedTranscriptSegment> recognizedSegments,
        bool commercialSafeMode,
        CancellationToken cancellationToken)
    {
        StageRunRecord diarizationStageRun = StageRunRecord.Start(projectId, "diarization", DateTimeOffset.UtcNow);
        await stageRunStore.CreateAsync(diarizationStageRun, cancellationToken).ConfigureAwait(false);

        try
        {
            IReadOnlyList<DiarizedSpeakerTurn> diarizedTurns = await diarizationEngine.DiarizeAsync(
                normalizedAudioPath,
                durationSeconds,
                regions,
                commercialSafeMode,
                cancellationToken).ConfigureAwait(false);

            if (diarizedTurns.Count == 0)
            {
                return await CreateDefaultSpeakerAssignmentAsync(projectId, recognizedSegments, cancellationToken).ConfigureAwait(false);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var speakerData = diarizedTurns
                .OrderBy(turn => turn.StartSeconds)
                .GroupBy(turn => turn.NormalizedSpeakerKey, StringComparer.OrdinalIgnoreCase)
                .Select((group, index) =>
                {
                    ProjectSpeaker speaker = ProjectSpeaker.Create(projectId, $"Speaker {index + 1}", now.AddMilliseconds(index));
                    return new { SpeakerKey = group.Key, Speaker = speaker, SpeakerId = speaker.Id };
                })
                .ToArray();

            ProjectSpeaker[] speakers = speakerData.Select(entry => entry.Speaker).ToArray();
            Dictionary<string, Guid> speakerIdsByKey = speakerData.ToDictionary(
                entry => entry.SpeakerKey,
                entry => entry.SpeakerId,
                StringComparer.OrdinalIgnoreCase);

            SpeakerTurn[] turns = diarizedTurns
                .OrderBy(turn => turn.StartSeconds)
                .Select(turn => SpeakerTurn.Create(
                    projectId,
                    speakerIdsByKey[turn.NormalizedSpeakerKey],
                    turn.StartSeconds,
                    turn.EndSeconds,
                    turn.Confidence,
                    turn.HasOverlap,
                    diarizationStageRun.Id))
                .ToArray();

            await speakerRepository.ReplaceDiarizationAsync(
                projectId,
                speakers,
                turns,
                cancellationToken).ConfigureAwait(false);

            diarizationStageRun = ApplyRuntimeExecutionSummary(diarizationStageRun, diarizationEngine)
                .Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(diarizationStageRun, cancellationToken).ConfigureAwait(false);

            return new SpeakerAssignmentResult(
                speakers,
                turns,
                AssignTranscriptSegmentsToSpeakers(recognizedSegments, speakers, turns));
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException or TaskCanceledException)
            {
                throw;
            }

            StageRunRecord failed = ApplyRuntimeExecutionSummary(diarizationStageRun, diarizationEngine)
                .Fail(DateTimeOffset.UtcNow, ex.Message);
            await stageRunStore.UpdateAsync(failed, cancellationToken).ConfigureAwait(false);

            return await CreateDefaultSpeakerAssignmentAsync(projectId, recognizedSegments, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SpeakerAssignmentResult> CreateDefaultSpeakerAssignmentAsync(
        Guid projectId,
        IReadOnlyList<RecognizedTranscriptSegment> recognizedSegments,
        CancellationToken cancellationToken)
    {
        ProjectSpeaker defaultSpeaker = await speakerRepository.EnsureDefaultSpeakerAsync(projectId, cancellationToken).ConfigureAwait(false);
        return CreateDefaultSpeakerAssignment(defaultSpeaker, recognizedSegments);
    }

    private static SpeakerAssignmentResult CreateDefaultSpeakerAssignment(
        ProjectSpeaker speaker,
        IReadOnlyList<RecognizedTranscriptSegment> recognizedSegments)
    {
        Dictionary<int, Guid> segmentSpeakerIdsByIndex = recognizedSegments
            .OrderBy(segment => segment.Index)
            .ToDictionary(segment => segment.Index, _ => speaker.Id);
        return new SpeakerAssignmentResult([ speaker ], [], segmentSpeakerIdsByIndex);
    }

    private static Dictionary<int, Guid> AssignTranscriptSegmentsToSpeakers(
        IReadOnlyList<RecognizedTranscriptSegment> recognizedSegments,
        IReadOnlyList<ProjectSpeaker> speakers,
        IReadOnlyList<SpeakerTurn> turns)
    {
        Guid? fallbackSpeakerId = speakers.OrderBy(speaker => speaker.CreatedAtUtc).FirstOrDefault()?.Id;
        var speakerIdsBySegmentIndex = new Dictionary<int, Guid>();

        foreach (RecognizedTranscriptSegment segment in recognizedSegments.OrderBy(segment => segment.Index))
        {
            SpeakerTurn? bestTurn = turns
                .Select(turn => new
                {
                    Turn = turn,
                    OverlapSeconds = GetOverlapSeconds(segment.StartSeconds, segment.EndSeconds, turn.StartSeconds, turn.EndSeconds)
                })
                .Where(entry => entry.OverlapSeconds > 0d)
                .OrderByDescending(entry => entry.OverlapSeconds)
                .ThenByDescending(entry => entry.Turn.Confidence ?? -1d)
                .ThenBy(entry => entry.Turn.StartSeconds)
                .Select(entry => entry.Turn)
                .FirstOrDefault();

            if (bestTurn is not null)
            {
                speakerIdsBySegmentIndex[segment.Index] = bestTurn.SpeakerId;
            }
            else if (fallbackSpeakerId is Guid speakerId)
            {
                speakerIdsBySegmentIndex[segment.Index] = speakerId;
            }
        }

        return speakerIdsBySegmentIndex;
    }

    private static double GetOverlapSeconds(
        double leftStartSeconds,
        double leftEndSeconds,
        double rightStartSeconds,
        double rightEndSeconds)
    {
        double overlap = Math.Min(leftEndSeconds, rightEndSeconds) - Math.Max(leftStartSeconds, rightStartSeconds);
        return overlap > 0d ? overlap : 0d;
    }

    private static ClipRange ResolveReferenceClipRange(
        TranscriptProjectState state,
        ExtractReferenceClipRequest request)
    {
        SpeakerTurn? turn = request.SpeakerTurnId is Guid speakerTurnId
            ? state.SpeakerTurns.FirstOrDefault(candidate => candidate.Id == speakerTurnId && candidate.SpeakerId == request.SpeakerId)
            : state.SpeakerTurns
                .Where(candidate => candidate.SpeakerId == request.SpeakerId)
                .OrderBy(candidate => candidate.HasOverlap)
                .ThenByDescending(candidate => candidate.Confidence ?? -1d)
                .ThenByDescending(candidate => candidate.EndSeconds - candidate.StartSeconds)
                .FirstOrDefault();

        if (turn is not null)
        {
            return new ClipRange(turn.StartSeconds, turn.EndSeconds);
        }

        TranscriptSegment? segment = state.TranscriptSegments
            .Where(candidate => candidate.SpeakerId == request.SpeakerId)
            .OrderByDescending(candidate => candidate.EndSeconds - candidate.StartSeconds)
            .FirstOrDefault();
        if (segment is not null)
        {
            return new ClipRange(segment.StartSeconds, segment.EndSeconds);
        }

        throw new InvalidOperationException("No speaker turn or transcript segment is available for reference clip extraction.");
    }

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

    private static TranslationExecutionMetadata? GetTranslationExecutionMetadata(object stageEngine) =>
        stageEngine is ITranslationExecutionMetadataReporter reporter
            ? reporter.LastExecutionMetadata
            : null;

    private static string NormalizeRequiredTranscriptLanguageCode(string languageCode)
    {
        string normalized = NormalizeTranscriptLanguageCode(languageCode)
            ?? throw new InvalidOperationException("Transcript language is required.");
        return normalized;
    }

    private static string NormalizeTranslationTargetLanguageCode(string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            throw new InvalidOperationException("Translation target language is required.");
        }

        return targetLanguage.Trim().ToLowerInvariant();
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
            _ => throw new InvalidOperationException("Milestone 9 currently supports English or Spanish transcript language selection.")
        };
    }

    private static string? ResolveSelectedTranslationTargetLanguage(
        string? sourceLanguage,
        string? requestedTranslationTargetLanguage,
        IReadOnlyList<TranslationTargetLanguageOption> supportedTargetLanguages)
    {
        string? normalizedRequestedTargetLanguage = NormalizeTranslationTargetLanguageCodeOrNull(requestedTranslationTargetLanguage);
        if (normalizedRequestedTargetLanguage is not null &&
            supportedTargetLanguages.Any(option => string.Equals(option.LanguageCode, normalizedRequestedTargetLanguage, StringComparison.Ordinal)))
        {
            return normalizedRequestedTargetLanguage;
        }

        TranslationTargetLanguageOption? defaultTarget = supportedTargetLanguages.FirstOrDefault(option =>
            option.IsAvailable &&
            IsPreferredDefaultTarget(sourceLanguage, option.LanguageCode));
        if (defaultTarget is not null)
        {
            return defaultTarget.LanguageCode;
        }

        TranslationTargetLanguageOption? firstAvailable = supportedTargetLanguages.FirstOrDefault(option => option.IsAvailable);
        if (firstAvailable is not null)
        {
            return firstAvailable.LanguageCode;
        }

        return supportedTargetLanguages.FirstOrDefault()?.LanguageCode;
    }

    private static bool IsPreferredDefaultTarget(string? sourceLanguage, string targetLanguage)
    {
        return (NormalizeTranscriptLanguageCode(sourceLanguage), NormalizeTranslationTargetLanguageCode(targetLanguage)) switch
        {
            (EnglishLanguageCode, SpanishLanguageCode) => true,
            (SpanishLanguageCode, EnglishLanguageCode) => true,
            _ => false
        };
    }

    private static string? NormalizeTranslationTargetLanguageCodeOrNull(string? targetLanguage) =>
        string.IsNullOrWhiteSpace(targetLanguage)
            ? null
            : targetLanguage.Trim().ToLowerInvariant();

    private static IReadOnlySet<int> BuildStaleTranslatedSegmentIndices(
        TranscriptRevision? currentTranscriptRevision,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        TranslationRevision? currentTranslationRevision,
        IReadOnlyList<TranslatedSegment> translatedSegments)
    {
        if (currentTranscriptRevision is null || currentTranslationRevision is null)
        {
            return new HashSet<int>();
        }

        bool revisionChanged = currentTranslationRevision.SourceTranscriptRevisionId != currentTranscriptRevision.Id;
        Dictionary<int, TranslatedSegment> translatedSegmentsByIndex = translatedSegments
            .GroupBy(segment => segment.SegmentIndex)
            .ToDictionary(group => group.Key, group => group.Last());
        var staleIndices = new HashSet<int>();

        foreach (TranscriptSegment transcriptSegment in transcriptSegments.OrderBy(segment => segment.SegmentIndex))
        {
            if (!translatedSegmentsByIndex.TryGetValue(transcriptSegment.SegmentIndex, out TranslatedSegment? translatedSegment))
            {
                if (revisionChanged)
                {
                    staleIndices.Add(transcriptSegment.SegmentIndex);
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(translatedSegment.SourceSegmentHash))
            {
                if (revisionChanged)
                {
                    staleIndices.Add(transcriptSegment.SegmentIndex);
                }

                continue;
            }

            if (!string.Equals(
                    translatedSegment.SourceSegmentHash,
                    ComputeSourceSegmentHash(transcriptSegment),
                    StringComparison.Ordinal))
            {
                staleIndices.Add(transcriptSegment.SegmentIndex);
            }
        }

        return staleIndices;
    }

    private static string ComputeSourceSegmentHash(TranscriptSegment segment)
    {
        string payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{segment.SegmentIndex}|{segment.StartSeconds:F6}|{segment.EndSeconds:F6}|{segment.Text.Trim()}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
                        segment.Text,
                        segment.SpeakerId))
                    .ToArray());
    }

    private sealed record TranslationRevisionArtifactDocument(
        Guid RevisionId,
        Guid? StageRunId,
        Guid SourceTranscriptRevisionId,
        string TargetLanguage,
        string? TranslationProvider,
        string? ModelId,
        string? ExecutionProvider,
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
                revision.TranslationProvider,
                revision.ModelId,
                revision.ExecutionProvider,
                revision.RevisionNumber,
                provenance,
                revision.CreatedAtUtc,
                segments
                    .OrderBy(segment => segment.SegmentIndex)
                    .Select(segment => new TranslatedSegmentArtifactDocument(
                        segment.SegmentIndex,
                        segment.StartSeconds,
                        segment.EndSeconds,
                        segment.Text,
                        segment.SourceSegmentHash))
                    .ToArray());
    }

    private sealed record TranscriptSegmentArtifactDocument(
        int SegmentIndex,
        double StartSeconds,
        double EndSeconds,
        string Text,
        Guid? SpeakerId);

    private sealed record TranslatedSegmentArtifactDocument(
        int SegmentIndex,
        double StartSeconds,
        double EndSeconds,
        string Text,
        string? SourceSegmentHash);

    private sealed record SpeakerAssignmentResult(
        IReadOnlyList<ProjectSpeaker> Speakers,
        IReadOnlyList<SpeakerTurn> Turns,
        IReadOnlyDictionary<int, Guid> SegmentSpeakerIdsByIndex);

    private sealed record ClipRange(
        double StartSeconds,
        double EndSeconds);
}
