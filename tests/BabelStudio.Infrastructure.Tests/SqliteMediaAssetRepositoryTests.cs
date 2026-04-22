using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;
using BabelStudio.Infrastructure.Persistence.Sqlite;

namespace BabelStudio.Infrastructure.Tests;

public sealed class SqliteMediaAssetRepositoryTests
{
    [Fact]
    public async Task Repository_round_trips_media_asset_and_artifacts_for_reopen()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "Reopen.babelstudio");
        var database = new SqliteProjectDatabase(projectRoot);
        var projectRepository = new SqliteProjectRepository(database);
        var mediaRepository = new SqliteMediaAssetRepository(database);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var project = new BabelProject(Guid.NewGuid(), "Reopen", now, now);
        var mediaAsset = new MediaAsset(Guid.NewGuid(), project.Id, @"D:\media\source.mp4", "source.mp4", "abc", 123, now, "mp4", 1.2, true, true, now);
        Guid stageRunId = Guid.NewGuid();
        var audioArtifact = new ProjectArtifact(Guid.NewGuid(), project.Id, mediaAsset.Id, ArtifactKind.NormalizedAudio, "media/normalized_audio.wav", "def", 456, 1.1, 48000, 1, now, stageRunId, "generated-audio");
        var waveformArtifact = new ProjectArtifact(Guid.NewGuid(), project.Id, mediaAsset.Id, ArtifactKind.WaveformSummary, "artifacts/waveform/normalized_audio.waveform.json", "ghi", 789, 1.1, 48000, 1, now);

        try
        {
            await projectRepository.InitializeAsync(project, CancellationToken.None);
            await mediaRepository.SaveAsync(mediaAsset, CancellationToken.None);
            await mediaRepository.SaveArtifactAsync(audioArtifact, CancellationToken.None);
            await mediaRepository.SaveArtifactAsync(waveformArtifact, CancellationToken.None);

            BabelProject? reopenedProject = await projectRepository.GetAsync(CancellationToken.None);
            MediaAsset? reopenedAsset = await mediaRepository.GetPrimaryAsync(project.Id, CancellationToken.None);
            IReadOnlyList<ProjectArtifact> reopenedArtifacts = await mediaRepository.GetArtifactsAsync(project.Id, CancellationToken.None);

            Assert.NotNull(reopenedProject);
            Assert.NotNull(reopenedAsset);
            Assert.Equal(project.Name, reopenedProject!.Name);
            Assert.Equal(mediaAsset.SourceFilePath, reopenedAsset!.SourceFilePath);
            Assert.Equal(mediaAsset.SourceFileName, reopenedAsset!.SourceFileName);
            Assert.Equal(2, reopenedArtifacts.Count);
            Assert.Contains(reopenedArtifacts, artifact => artifact.Kind == ArtifactKind.NormalizedAudio);
            Assert.Contains(reopenedArtifacts, artifact => artifact.Kind == ArtifactKind.WaveformSummary);
            ProjectArtifact reopenedAudio = Assert.Single(reopenedArtifacts, artifact => artifact.Kind == ArtifactKind.NormalizedAudio);
            Assert.Equal(stageRunId, reopenedAudio.StageRunId);
            Assert.Equal("generated-audio", reopenedAudio.Provenance);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateSourcePathAsync_persists_relocated_path()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "Relocate.babelstudio");
        var database = new SqliteProjectDatabase(projectRoot);
        var projectRepository = new SqliteProjectRepository(database);
        var mediaRepository = new SqliteMediaAssetRepository(database);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var project = new BabelProject(Guid.NewGuid(), "Relocate", now, now);
        var mediaAsset = new MediaAsset(Guid.NewGuid(), project.Id, @"D:\media\source.mp4", "source.mp4", "abc", 123, now, "mp4", 1.2, true, true, now);

        try
        {
            await projectRepository.InitializeAsync(project, CancellationToken.None);
            await mediaRepository.SaveAsync(mediaAsset, CancellationToken.None);
            await mediaRepository.UpdateSourcePathAsync(mediaAsset.Id, @"D:\media\moved\source.mp4", "source.mp4", CancellationToken.None);

            MediaAsset? reopenedAsset = await mediaRepository.GetPrimaryAsync(project.Id, CancellationToken.None);

            Assert.NotNull(reopenedAsset);
            Assert.Equal(@"D:\media\moved\source.mp4", reopenedAsset!.SourceFilePath);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
