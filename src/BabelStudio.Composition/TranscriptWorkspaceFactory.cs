using BabelStudio.Application.Projects;
using BabelStudio.Application.Transcripts;
using BabelStudio.Composition.Runtime.Planning;
using BabelStudio.Infrastructure.Persistence.Repositories;
using BabelStudio.Infrastructure.Settings;
using BabelStudio.Inference.Onnx;
using BabelStudio.Inference.Onnx.Runtime.Planning;
using BabelStudio.Inference.Onnx.SileroVad;
using BabelStudio.Inference.Onnx.Whisper;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;
using BabelStudio.Infrastructure.FileSystem;
using BabelStudio.Infrastructure.Persistence.Sqlite;
using BabelStudio.Inference.Onnx.OpusMt;
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
        BundledModelManifestRegistry manifestRegistry = LoadManifestRegistry();
        var recordStore = new LocalModelCacheRecordStore(new BabelStudioStoragePaths());
        var modelInventory = new CompositeModelCacheInventory(
            new LocalModelCacheInventory(recordStore),
            new BundledManifestModelCacheInventory(manifestRegistry));
        var runtimePlanner = new RuntimePlanner(
            manifestRegistry,
            new CommercialSafeEvaluator(),
            new MachineHardwareProfileProvider(),
            new OnnxExecutionProviderDiscovery(),
            new OnnxExecutionProviderSmokeTester(),
            modelInventory);
        var modelPathResolver = new BenchmarkModelPathResolver(manifestRegistry);

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
            new SqliteTranslationRepository(database),
            new SqliteProjectStageRunStore(database),
            mediaRepository,
            artifactStore,
            new Sha256FileFingerprintService(),
            new SileroVadSpeechRegionDetector(runtimePlanner, modelPathResolver),
            new WhisperOnnxAudioTranscriptionEngine(runtimePlanner, modelPathResolver),
            new OpusMtTranslationEngine(runtimePlanner, modelPathResolver));
    }

    private static BundledModelManifestRegistry LoadManifestRegistry()
    {
        if (BundledModelManifestRegistry.TryLoadDefault(out BundledModelManifestRegistry? registry, out string? error) &&
            registry is not null)
        {
            return registry;
        }

        throw new InvalidOperationException(error ?? "Bundled model manifest was not found.");
    }
}
