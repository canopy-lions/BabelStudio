using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;

namespace BabelStudio.Application.Tests;

public sealed class ProjectMediaIngestServiceTests
{
    [Fact]
    public async Task CreateAsync_registers_media_and_artifacts()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BabelStudio.Application.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "sample.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        var projectRepository = new FakeProjectRepository();
        var mediaRepository = new FakeMediaAssetRepository();
        var artifactStore = new FakeArtifactStore();
        var service = new ProjectMediaIngestService(
            projectRepository,
            mediaRepository,
            artifactStore,
            new FakeMediaProbe(),
            new FakeAudioExtractionService(),
            new FakeWaveformSummaryGenerator(),
            new FakeFileFingerprintService());

        CreateProjectFromMediaResult result = await service.CreateAsync(
            new CreateProjectFromMediaRequest("Sample Project", sourcePath),
            CancellationToken.None);

        Assert.Equal("Sample Project", result.Project.Name);
        Assert.Equal("sample.mp4", result.SourceReference.OriginalFileName);
        Assert.Contains(ProjectArtifactPaths.ManifestRelativePath, artifactStore.JsonWrites.Keys);
        Assert.Contains(ProjectArtifactPaths.SourceReferenceRelativePath, artifactStore.JsonWrites.Keys);
        Assert.Equal(2, mediaRepository.Artifacts.Count);
        Assert.Equal(ArtifactKind.NormalizedAudio, mediaRepository.Artifacts[0].Kind);
        Assert.Equal(ArtifactKind.WaveformSummary, mediaRepository.Artifacts[1].Kind);
    }

    [Fact]
    public async Task OpenAsync_reports_missing_source_file_clearly()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var project = new BabelProject(Guid.NewGuid(), "Missing Source", now, now);
        var mediaAsset = new MediaAsset(Guid.NewGuid(), project.Id, "missing.mp4", "hash", 100, now, "mp4", 1.0, true, true, now);
        var projectRepository = new FakeProjectRepository(project);
        var mediaRepository = new FakeMediaAssetRepository(mediaAsset);
        var artifactStore = new FakeArtifactStore();
        artifactStore.Seed(ProjectArtifactPaths.SourceReferenceRelativePath, new SourceMediaReference(
            @"C:\media\missing.mp4",
            "missing.mp4",
            new FileFingerprint("hash", 100, now),
            new MediaProbeSnapshot("mp4", "mp4", 1.0, null, [new MediaAudioStream(0, "aac", 2, 44100, 1.0)], []),
            now));
        mediaRepository.Artifacts.Add(new ProjectArtifact(Guid.NewGuid(), project.Id, mediaAsset.Id, ArtifactKind.NormalizedAudio, "media/normalized_audio.wav", "artifact-hash", 100, 1.0, 48000, 1, now));

        var service = new ProjectMediaIngestService(
            projectRepository,
            mediaRepository,
            artifactStore,
            new FakeMediaProbe(),
            new FakeAudioExtractionService(),
            new FakeWaveformSummaryGenerator(),
            new FakeFileFingerprintService());

        OpenProjectResult result = await service.OpenAsync(CancellationToken.None);

        Assert.Equal(SourceMediaStatus.Missing, result.SourceStatus);
        Assert.Contains("Source media file was not found", result.SourceStatusMessage);
        Assert.Single(result.Artifacts);
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        private BabelProject? project;

        public FakeProjectRepository(BabelProject? project = null)
        {
            this.project = project;
        }

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

        public FakeMediaAssetRepository(MediaAsset? mediaAsset = null)
        {
            this.mediaAsset = mediaAsset;
        }

        public List<ProjectArtifact> Artifacts { get; } = [];

        public Task SaveAsync(MediaAsset asset, CancellationToken cancellationToken)
        {
            mediaAsset = asset;
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
            Task.FromResult<IReadOnlyList<ProjectArtifact>>(Artifacts);
    }

    private sealed class FakeArtifactStore : IArtifactStore
    {
        private readonly Dictionary<string, object> reads = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, object> JsonWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task EnsureLayoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ArtifactWriteHandle CreateWriteHandle(string relativePath) =>
            new(relativePath, Path.Combine("project", relativePath), Path.Combine("project", "temp", Path.GetFileName(relativePath)));

        public Task CommitAsync(ArtifactWriteHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken cancellationToken)
        {
            JsonWrites[relativePath] = value!;
            reads[relativePath] = value!;
            return Task.CompletedTask;
        }

        public Task<T?> ReadJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
        {
            if (reads.TryGetValue(relativePath, out object? value))
            {
                return Task.FromResult((T?)value);
            }

            return Task.FromResult<T?>(default);
        }

        public string GetPath(string relativePath) => Path.Combine("project", relativePath);

        public bool Exists(string relativePath) => reads.ContainsKey(relativePath);

        public void Seed<T>(string relativePath, T value) where T : notnull
        {
            reads[relativePath] = value;
        }
    }

    private sealed class FakeMediaProbe : IMediaProbe
    {
        public Task<MediaProbeSnapshot> ProbeAsync(string sourcePath, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaProbeSnapshot(
                "mov,mp4,m4a,3gp,3g2,mj2",
                "QuickTime / MOV",
                1.25,
                1024,
                [new MediaAudioStream(0, "aac", 2, 44100, 1.25)],
                [new MediaVideoStream(1, "h264", 64, 64, 24, 1.25)]));
    }

    private sealed class FakeAudioExtractionService : IAudioExtractionService
    {
        public async Task<AudioExtractionResult> ExtractNormalizedAudioAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, [1, 2, 3, 4], cancellationToken);
            return new AudioExtractionResult(destinationPath, 1.25, 48000, 1, 60000);
        }
    }

    private sealed class FakeWaveformSummaryGenerator : IWaveformSummaryGenerator
    {
        public Task<WaveformSummary> GenerateAsync(string audioPath, CancellationToken cancellationToken) =>
            Task.FromResult(new WaveformSummary(4, 48000, 1, 1.25, [0.1f, 0.4f, 0.8f, 0.2f]));
    }

    private sealed class FakeFileFingerprintService : IFileFingerprintService
    {
        public Task<FileFingerprint> ComputeAsync(string path, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return Task.FromResult(new FileFingerprint("missing-hash", 0, DateTimeOffset.UtcNow));
            }

            return Task.FromResult(new FileFingerprint("computed-hash", new FileInfo(path).Length, DateTimeOffset.UtcNow));
        }
    }
}
