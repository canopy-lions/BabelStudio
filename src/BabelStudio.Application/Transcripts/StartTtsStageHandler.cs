using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Transcript;
using BabelStudio.Domain.Translation;
using BabelStudio.Domain.Tts;

namespace BabelStudio.Application.Transcripts;

public sealed record StartTtsStageRequest(
    Guid ProjectId,
    MediaAsset MediaAsset,
    Guid SpeakerId,
    string TargetLanguage,
    VoiceAssignment VoiceAssignment,
    IReadOnlyList<TranscriptSegment> TranscriptSegments,
    IReadOnlyList<TranslatedSegment> TranslatedSegments,
    IReadOnlySet<int>? SegmentIndices = null);

public sealed record StartTtsStageResult(
    StageRunRecord StageRun,
    IReadOnlyList<TtsTake> Takes);

public sealed class StartTtsStageHandler
{
    private const double DurationWarningThreshold = 0.10d;

    private readonly ITtsEngine ttsEngine;
    private readonly IVoiceCatalog voiceCatalog;
    private readonly IArtifactStore artifactStore;
    private readonly IFileFingerprintService fileFingerprintService;
    private readonly IMediaAssetRepository mediaAssetRepository;
    private readonly ITtsTakeRepository ttsTakeRepository;
    private readonly IProjectStageRunStore stageRunStore;

    public StartTtsStageHandler(
        ITtsEngine ttsEngine,
        IVoiceCatalog voiceCatalog,
        IArtifactStore artifactStore,
        IFileFingerprintService fileFingerprintService,
        IMediaAssetRepository mediaAssetRepository,
        ITtsTakeRepository ttsTakeRepository,
        IProjectStageRunStore stageRunStore)
    {
        this.ttsEngine = ttsEngine;
        this.voiceCatalog = voiceCatalog;
        this.artifactStore = artifactStore;
        this.fileFingerprintService = fileFingerprintService;
        this.mediaAssetRepository = mediaAssetRepository;
        this.ttsTakeRepository = ttsTakeRepository;
        this.stageRunStore = stageRunStore;
    }

    public async Task<StartTtsStageResult> HandleAsync(
        StartTtsStageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string voiceId = ResolveVoiceId(request.VoiceAssignment);
        if (!voiceCatalog.TryGetVoice(voiceId, out VoiceCatalogEntry? voice))
        {
            throw new InvalidOperationException($"Voicepack '{voiceId}' is not available.");
        }

        StageRunRecord stageRun = StageRunRecord.Start(request.ProjectId, "tts", DateTimeOffset.UtcNow);
        await stageRunStore.CreateAsync(stageRun, cancellationToken).ConfigureAwait(false);

        var takes = new List<TtsTake>();
        try
        {
            Dictionary<int, TranscriptSegment> transcriptSegmentsByIndex = request.TranscriptSegments
                .Where(segment => segment.SpeakerId == request.SpeakerId)
                .ToDictionary(segment => segment.SegmentIndex);
            IEnumerable<TranslatedSegment> targetSegments = request.TranslatedSegments
                .OrderBy(segment => segment.SegmentIndex)
                .Where(segment => transcriptSegmentsByIndex.ContainsKey(segment.SegmentIndex));

            if (request.SegmentIndices is { Count: > 0 } requestedIndices)
            {
                targetSegments = targetSegments.Where(segment => requestedIndices.Contains(segment.SegmentIndex));
            }

            foreach (TranslatedSegment translatedSegment in targetSegments)
            {
                TranscriptSegment sourceSegment = transcriptSegmentsByIndex[translatedSegment.SegmentIndex];
                TtsTake take = await SynthesizeSegmentAsync(
                    request,
                    stageRun.Id,
                    translatedSegment,
                    sourceSegment,
                    voice,
                    cancellationToken).ConfigureAwait(false);
                takes.Add(take);
            }

            stageRun = ApplyRuntimeExecutionSummary(stageRun, ttsEngine)
                .Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(stageRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageRunRecord failed = ApplyRuntimeExecutionSummary(stageRun, ttsEngine)
                .Fail(DateTimeOffset.UtcNow, ex.Message);
            await stageRunStore.UpdateAsync(failed, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new StartTtsStageResult(stageRun, takes);
    }

    private async Task<TtsTake> SynthesizeSegmentAsync(
        StartTtsStageRequest request,
        Guid stageRunId,
        TranslatedSegment translatedSegment,
        TranscriptSegment sourceSegment,
        VoiceCatalogEntry voice,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TtsTake> existingTakes = await ttsTakeRepository
            .GetBySegmentAsync(translatedSegment.Id, cancellationToken)
            .ConfigureAwait(false);
        int nextTakeNumber = existingTakes.Count + 1;
        string relativePath = ProjectArtifactPaths.GetTtsTakeRelativePath(
            request.SpeakerId,
            translatedSegment.Id,
            nextTakeNumber);
        ArtifactWriteHandle writeHandle = artifactStore.CreateWriteHandle(relativePath);
        bool committed = false;

        TtsSynthesisResult result = await ttsEngine.SynthesizeAsync(
            new TtsSynthesisRequest(translatedSegment.Text, request.TargetLanguage, voice),
            cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(writeHandle.TemporaryPath)!);
            await File.WriteAllBytesAsync(writeHandle.TemporaryPath, result.WavBytes, cancellationToken)
                .ConfigureAwait(false);
            await artifactStore.CommitAsync(writeHandle, cancellationToken).ConfigureAwait(false);
            committed = true;
        }
        finally
        {
            if (!committed && File.Exists(writeHandle.TemporaryPath))
            {
                File.Delete(writeHandle.TemporaryPath);
            }
        }

        string finalPath = artifactStore.GetPath(relativePath);
        FileFingerprint fingerprint = await fileFingerprintService
            .ComputeAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        double? durationSeconds = result.SampleRate > 0
            ? (double)result.DurationSamples / result.SampleRate
            : null;
        double? durationOverrunRatio = ComputeDurationOverrunRatio(sourceSegment, durationSeconds);

        var artifact = new ProjectArtifact(
            Guid.NewGuid(),
            request.ProjectId,
            request.MediaAsset.Id,
            ArtifactKind.TtsTake,
            relativePath,
            fingerprint.Sha256,
            fingerprint.SizeBytes,
            durationSeconds,
            result.SampleRate,
            ChannelCount: 1,
            DateTimeOffset.UtcNow,
            stageRunId,
            $"tts:{result.ModelId}:{result.VoiceId}");
        await mediaAssetRepository.SaveArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);

        TtsTake take = TtsTake.Create(
                request.ProjectId,
                request.VoiceAssignment.Id,
                translatedSegment.Id,
                translatedSegment.SegmentIndex,
                TtsTextHash.Compute(translatedSegment.SegmentIndex, translatedSegment.Text))
            .Complete(
                artifact.Id,
                stageRunId,
                result.DurationSamples,
                result.SampleRate,
                result.Provider,
                result.ModelId,
                result.VoiceId,
                durationOverrunRatio);
        await ttsTakeRepository.SaveAsync(take, cancellationToken).ConfigureAwait(false);
        return take;
    }

    private static string ResolveVoiceId(VoiceAssignment assignment) =>
        string.IsNullOrWhiteSpace(assignment.VoiceVariant)
            ? assignment.VoiceModelId
            : assignment.VoiceVariant;

    private static double? ComputeDurationOverrunRatio(TranscriptSegment sourceSegment, double? ttsDurationSeconds)
    {
        double sourceDurationSeconds = sourceSegment.EndSeconds - sourceSegment.StartSeconds;
        if (ttsDurationSeconds is null || sourceDurationSeconds <= 0d)
        {
            return null;
        }

        double ratio = (ttsDurationSeconds.Value - sourceDurationSeconds) / sourceDurationSeconds;
        return ratio <= 0d ? 0d : ratio;
    }

    public static bool HasDurationWarning(TtsTake take) =>
        take.DurationOverrunRatio is > DurationWarningThreshold;

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
}
