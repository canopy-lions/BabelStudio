using BabelStudio.Application.Contracts;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;

namespace BabelStudio.Application.Projects;

public sealed class ProjectMediaIngestService
{
    private readonly IProjectRepository projectRepository;
    private readonly IMediaAssetRepository mediaAssetRepository;
    private readonly IArtifactStore artifactStore;
    private readonly IMediaProbe mediaProbe;
    private readonly IAudioExtractionService audioExtractionService;
    private readonly IWaveformSummaryGenerator waveformSummaryGenerator;
    private readonly IFileFingerprintService fileFingerprintService;

    public ProjectMediaIngestService(
        IProjectRepository projectRepository,
        IMediaAssetRepository mediaAssetRepository,
        IArtifactStore artifactStore,
        IMediaProbe mediaProbe,
        IAudioExtractionService audioExtractionService,
        IWaveformSummaryGenerator waveformSummaryGenerator,
        IFileFingerprintService fileFingerprintService)
    {
        this.projectRepository = projectRepository;
        this.mediaAssetRepository = mediaAssetRepository;
        this.artifactStore = artifactStore;
        this.mediaProbe = mediaProbe;
        this.audioExtractionService = audioExtractionService;
        this.waveformSummaryGenerator = waveformSummaryGenerator;
        this.fileFingerprintService = fileFingerprintService;
    }

    public async Task<CreateProjectFromMediaResult> CreateAsync(
        CreateProjectFromMediaRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceMediaPath);

        string fullSourcePath = Path.GetFullPath(request.SourceMediaPath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("Source media file was not found.", fullSourcePath);
        }

        await artifactStore.EnsureLayoutAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var project = new BabelProject(Guid.NewGuid(), request.ProjectName.Trim(), now, now);
        await projectRepository.InitializeAsync(project, cancellationToken).ConfigureAwait(false);

        await artifactStore.WriteJsonAsync(
            ProjectArtifactPaths.ManifestRelativePath,
            ProjectManifest.FromProject(project),
            cancellationToken).ConfigureAwait(false);

        MediaProbeSnapshot probe = await mediaProbe.ProbeAsync(fullSourcePath, cancellationToken).ConfigureAwait(false);
        FileFingerprint sourceFingerprint = await fileFingerprintService.ComputeAsync(fullSourcePath, cancellationToken).ConfigureAwait(false);

        var sourceReference = new SourceMediaReference(
            fullSourcePath,
            Path.GetFileName(fullSourcePath),
            sourceFingerprint,
            probe,
            now);

        await artifactStore.WriteJsonAsync(
            ProjectArtifactPaths.SourceReferenceRelativePath,
            sourceReference,
            cancellationToken).ConfigureAwait(false);

        var mediaAsset = new MediaAsset(
            Guid.NewGuid(),
            project.Id,
            sourceReference.OriginalFileName,
            sourceFingerprint.Sha256,
            sourceFingerprint.SizeBytes,
            sourceFingerprint.LastWriteTimeUtc,
            probe.FormatName,
            probe.DurationSeconds,
            probe.AudioStreams.Count > 0,
            probe.VideoStreams.Count > 0,
            now);

        await mediaAssetRepository.SaveAsync(mediaAsset, cancellationToken).ConfigureAwait(false);

        ArtifactWriteHandle audioWriteHandle = artifactStore.CreateWriteHandle(ProjectArtifactPaths.NormalizedAudioRelativePath);
        AudioExtractionResult extraction = await audioExtractionService.ExtractNormalizedAudioAsync(
            fullSourcePath,
            audioWriteHandle.TemporaryPath,
            cancellationToken).ConfigureAwait(false);
        await artifactStore.CommitAsync(audioWriteHandle, cancellationToken).ConfigureAwait(false);

        FileFingerprint audioFingerprint = await fileFingerprintService.ComputeAsync(
            audioWriteHandle.FinalPath,
            cancellationToken).ConfigureAwait(false);

        var audioArtifact = new ProjectArtifact(
            Guid.NewGuid(),
            project.Id,
            mediaAsset.Id,
            ArtifactKind.NormalizedAudio,
            ProjectArtifactPaths.NormalizedAudioRelativePath,
            audioFingerprint.Sha256,
            audioFingerprint.SizeBytes,
            extraction.DurationSeconds,
            extraction.SampleRate,
            extraction.ChannelCount,
            now);

        await mediaAssetRepository.SaveArtifactAsync(audioArtifact, cancellationToken).ConfigureAwait(false);

        WaveformSummary waveform = await waveformSummaryGenerator.GenerateAsync(
            audioWriteHandle.FinalPath,
            cancellationToken).ConfigureAwait(false);

        await artifactStore.WriteJsonAsync(
            ProjectArtifactPaths.WaveformSummaryRelativePath,
            waveform,
            cancellationToken).ConfigureAwait(false);

        FileFingerprint waveformFingerprint = await fileFingerprintService.ComputeAsync(
            artifactStore.GetPath(ProjectArtifactPaths.WaveformSummaryRelativePath),
            cancellationToken).ConfigureAwait(false);

        var waveformArtifact = new ProjectArtifact(
            Guid.NewGuid(),
            project.Id,
            mediaAsset.Id,
            ArtifactKind.WaveformSummary,
            ProjectArtifactPaths.WaveformSummaryRelativePath,
            waveformFingerprint.Sha256,
            waveformFingerprint.SizeBytes,
            waveform.DurationSeconds,
            waveform.SampleRate,
            waveform.ChannelCount,
            now);

        await mediaAssetRepository.SaveArtifactAsync(waveformArtifact, cancellationToken).ConfigureAwait(false);

        return new CreateProjectFromMediaResult(
            project,
            mediaAsset,
            sourceReference,
            audioArtifact,
            waveformArtifact);
    }

    public async Task<OpenProjectResult> OpenAsync(CancellationToken cancellationToken)
    {
        BabelProject? project = await projectRepository.GetAsync(cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new InvalidOperationException("Project database does not contain a project record.");
        }

        ProjectManifest? manifest = await artifactStore.ReadJsonAsync<ProjectManifest>(
            ProjectArtifactPaths.ManifestRelativePath,
            cancellationToken).ConfigureAwait(false);
        SourceMediaReference? sourceReference = await artifactStore.ReadJsonAsync<SourceMediaReference>(
            ProjectArtifactPaths.SourceReferenceRelativePath,
            cancellationToken).ConfigureAwait(false);

        MediaAsset? mediaAsset = await mediaAssetRepository.GetPrimaryAsync(project.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProjectArtifact> artifacts = await mediaAssetRepository.GetArtifactsAsync(project.Id, cancellationToken).ConfigureAwait(false);
        (SourceMediaStatus status, string? message) = await ResolveSourceStatusAsync(sourceReference, cancellationToken).ConfigureAwait(false);

        return new OpenProjectResult(
            project,
            mediaAsset,
            sourceReference,
            status,
            message,
            artifacts,
            manifest?.TranscriptLanguage);
    }

    private async Task<(SourceMediaStatus Status, string? Message)> ResolveSourceStatusAsync(
        SourceMediaReference? sourceReference,
        CancellationToken cancellationToken)
    {
        if (sourceReference is null)
        {
            return (SourceMediaStatus.Unknown, "Source media reference is missing from the project.");
        }

        if (!File.Exists(sourceReference.OriginalPath))
        {
            return (
                SourceMediaStatus.Missing,
                $"Source media file was not found at '{sourceReference.OriginalPath}'.");
        }

        FileFingerprint currentFingerprint = await fileFingerprintService.ComputeAsync(
            sourceReference.OriginalPath,
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(currentFingerprint.Sha256, sourceReference.Fingerprint.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return (
                SourceMediaStatus.Changed,
                $"Source media file contents changed since ingest: '{sourceReference.OriginalPath}'.");
        }

        return (SourceMediaStatus.Available, null);
    }
}
