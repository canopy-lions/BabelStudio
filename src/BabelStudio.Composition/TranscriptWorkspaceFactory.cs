using BabelStudio.Application.Projects;
using BabelStudio.Application.Transcripts;
using BabelStudio.Inference.Pipelines.Transcript;
using BabelStudio.Infrastructure.FileSystem;
using BabelStudio.Infrastructure.Persistence.Sqlite;
using BabelStudio.Media.Extraction;
using BabelStudio.Media.Probe;
using BabelStudio.Media.Waveforms;

namespace BabelStudio.Composition;

public sealed class TranscriptWorkspaceFactory
{
    public TranscriptProjectService Create(string projectRootPath)
    {
        var database = new SqliteProjectDatabase(projectRootPath);
        var artifactStore = new FileSystemArtifactStore(projectRootPath);
        var mediaRepository = new SqliteMediaAssetRepository(database);

        return new TranscriptProjectService(
            new ProjectMediaIngestService(
                new SqliteProjectRepository(database),
                mediaRepository,
                artifactStore,
                new FfmpegMediaProbe(ffmpegPath: null, ffprobePath: null),
                new FfmpegAudioExtractionService(ffmpegPath: null),
                new WaveformSummaryGenerator(),
                new Sha256FileFingerprintService()),
            new SqliteTranscriptRepository(database),
            new SqliteProjectStageRunStore(database),
            mediaRepository,
            artifactStore,
            new Sha256FileFingerprintService(),
            new ScriptedSpeechRegionDetector(),
            new ScriptedAudioTranscriptionEngine());
    }
}
