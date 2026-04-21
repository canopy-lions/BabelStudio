using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Transcript;

namespace BabelStudio.Application.Transcripts;

public sealed class TranscriptProjectService
{
    private readonly ProjectMediaIngestService projectMediaIngestService;
    private readonly ITranscriptRepository transcriptRepository;
    private readonly IProjectStageRunStore stageRunStore;
    private readonly IMediaAssetRepository mediaAssetRepository;
    private readonly IArtifactStore artifactStore;
    private readonly IFileFingerprintService fileFingerprintService;
    private readonly ISpeechRegionDetector speechRegionDetector;
    private readonly IAudioTranscriptionEngine transcriptionEngine;

    public TranscriptProjectService(
        ProjectMediaIngestService projectMediaIngestService,
        ITranscriptRepository transcriptRepository,
        IProjectStageRunStore stageRunStore,
        IMediaAssetRepository mediaAssetRepository,
        IArtifactStore artifactStore,
        IFileFingerprintService fileFingerprintService,
        ISpeechRegionDetector speechRegionDetector,
        IAudioTranscriptionEngine transcriptionEngine)
    {
        this.projectMediaIngestService = projectMediaIngestService;
        this.transcriptRepository = transcriptRepository;
        this.stageRunStore = stageRunStore;
        this.mediaAssetRepository = mediaAssetRepository;
        this.artifactStore = artifactStore;
        this.fileFingerprintService = fileFingerprintService;
        this.speechRegionDetector = speechRegionDetector;
        this.transcriptionEngine = transcriptionEngine;
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

        IReadOnlyList<StageRunRecord> stageRuns = await stageRunStore.ListByProjectAsync(
            openResult.Project.Id,
            cancellationToken).ConfigureAwait(false);

        return new TranscriptProjectState(openResult, currentRevision, segments, stageRuns);
    }

    public async Task<TranscriptProjectState> SaveEditsAsync(
        SaveTranscriptEditsRequest request,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState currentState = await OpenAsync(cancellationToken).ConfigureAwait(false);
        TranscriptRevision currentRevision = currentState.CurrentTranscriptRevision
            ?? throw new InvalidOperationException("The project does not contain a transcript revision.");

        if (currentRevision.Id != request.TranscriptRevisionId)
        {
            throw new InvalidOperationException("Transcript edits were based on an out-of-date revision.");
        }

        Dictionary<Guid, string> replacements = request.Segments.ToDictionary(
            segment => segment.SegmentId,
            segment => segment.Text,
            comparer: EqualityComparer<Guid>.Default);

        int nextRevisionNumber = await transcriptRepository.GetNextRevisionNumberAsync(
            currentState.ProjectState.Project.Id,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranscriptRevision editedRevision = TranscriptRevision.Create(
            currentState.ProjectState.Project.Id,
            stageRunId: null,
            nextRevisionNumber,
            now);

        TranscriptSegment[] editedSegments = currentState.TranscriptSegments
            .OrderBy(segment => segment.SegmentIndex)
            .Select(segment => TranscriptSegment.Create(
                editedRevision.Id,
                segment.SegmentIndex,
                segment.StartSeconds,
                segment.EndSeconds,
                replacements.TryGetValue(segment.Id, out string? replacement)
                    ? replacement
                    : segment.Text))
            .ToArray();

        await transcriptRepository.SaveRevisionAsync(editedRevision, editedSegments, cancellationToken).ConfigureAwait(false);

        string relativePath = ProjectArtifactPaths.GetTranscriptRevisionRelativePath(editedRevision.RevisionNumber);
        var artifactDocument = TranscriptRevisionArtifactDocument.From(editedRevision, editedSegments, provenance: "manual-edit");
        await artifactStore.WriteJsonAsync(relativePath, artifactDocument, cancellationToken).ConfigureAwait(false);

        FileFingerprint artifactFingerprint = await fileFingerprintService.ComputeAsync(
            artifactStore.GetPath(relativePath),
            cancellationToken).ConfigureAwait(false);

        MediaAsset mediaAsset = currentState.ProjectState.MediaAsset
            ?? throw new InvalidOperationException("The project does not contain a primary media asset.");

        var transcriptArtifact = new ProjectArtifact(
            Guid.NewGuid(),
            currentState.ProjectState.Project.Id,
            mediaAsset.Id,
            ArtifactKind.TranscriptRevision,
            relativePath,
            artifactFingerprint.Sha256,
            artifactFingerprint.SizeBytes,
            DurationSeconds: null,
            SampleRate: null,
            ChannelCount: null,
            now,
            StageRunId: null,
            Provenance: "manual-edit");

        await mediaAssetRepository.SaveArtifactAsync(transcriptArtifact, cancellationToken).ConfigureAwait(false);
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

            vadStageRun = vadStageRun.Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(vadStageRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageRunRecord failed = vadStageRun.Fail(DateTimeOffset.UtcNow, ex.Message);
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

            asrStageRun = asrStageRun.Complete(DateTimeOffset.UtcNow);
            await stageRunStore.UpdateAsync(asrStageRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageRunRecord failed = asrStageRun.Fail(DateTimeOffset.UtcNow, ex.Message);
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
        await WriteTranscriptArtifactAsync(projectId, mediaAsset, revision, segments, asrStageRun, cancellationToken).ConfigureAwait(false);
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
        StageRunRecord stageRun,
        CancellationToken cancellationToken)
    {
        string relativePath = ProjectArtifactPaths.GetTranscriptRevisionRelativePath(revision.RevisionNumber);
        await artifactStore.WriteJsonAsync(
            relativePath,
            TranscriptRevisionArtifactDocument.From(revision, segments, "generated-asr"),
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
            StageRunId: stageRun.Id,
            Provenance: "generated-asr");

        await mediaAssetRepository.SaveArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
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

    private sealed record TranscriptSegmentArtifactDocument(
        int SegmentIndex,
        double StartSeconds,
        double EndSeconds,
        string Text);
}
