using System.Diagnostics.CodeAnalysis;
using BabelStudio.Application.Projects;
using BabelStudio.Application.Transcripts;
using BabelStudio.Composition.Runtime.Planning;
using BabelStudio.Contracts.Pipeline;
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
using BabelStudio.Inference.Onnx.Kokoro;
using BabelStudio.Inference.Onnx.Madlad;
using BabelStudio.Inference.Onnx.OpusMt;
using BabelStudio.Inference.Onnx.SortFormer;
using BabelStudio.Inference.Onnx.Translation;
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
        var commercialSafeEvaluator = new CommercialSafeEvaluator();
        var runtimePlanner = new RuntimePlanner(
            manifestRegistry,
            commercialSafeEvaluator,
            new MachineHardwareProfileProvider(),
            new OnnxExecutionProviderDiscovery(),
            new OnnxExecutionProviderSmokeTester(),
            modelInventory);
        var modelPathResolver = new BenchmarkModelPathResolver(manifestRegistry);
        var translationLanguageRouter = new TranslationLanguageRouter(
            manifestRegistry,
            modelInventory,
            commercialSafeEvaluator);
        IVoiceCatalog voiceCatalog = CreateKokoroVoiceCatalog(modelPathResolver);

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
            new SqliteSpeakerRepository(database),
            artifactStore,
            new Pcm16WaveClipExtractor(),
            new Sha256FileFingerprintService(),
            new SileroVadSpeechRegionDetector(runtimePlanner, modelPathResolver),
            new SortFormerDiarizationEngine(runtimePlanner, modelPathResolver),
            new WhisperOnnxAudioTranscriptionEngine(runtimePlanner, modelPathResolver),
            translationLanguageRouter,
            new RoutedTranslationEngine(
                translationLanguageRouter,
                new OpusMtTranslationEngine(runtimePlanner, modelPathResolver),
                new MadladTranslationEngine(runtimePlanner, modelPathResolver)),
            new SqliteVoiceAssignmentRepository(database),
            new SqliteTtsTakeRepository(database),
            new KokoroTtsEngine(
                runtimePlanner,
                modelPathResolver,
                new EspeakNgPhonemizer()),
            voiceCatalog);
    }

    private static IVoiceCatalog CreateKokoroVoiceCatalog(BenchmarkModelPathResolver modelPathResolver)
    {
        try
        {
            BenchmarkModelCandidate candidate = modelPathResolver.ResolveSingle("kokoro-onnx");
            string? modelRootPath = candidate.RootDirectory ?? Path.GetDirectoryName(candidate.ModelPath);
            return string.IsNullOrWhiteSpace(modelRootPath)
                ? new EmptyVoiceCatalog()
                : KokoroVoiceCatalog.Load(modelRootPath);
        }
        catch
        {
            return new EmptyVoiceCatalog();
        }
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

    private sealed class EmptyVoiceCatalog : IVoiceCatalog
    {
        public IReadOnlyList<VoiceCatalogEntry> GetVoices(string? languageCode = null) => [];

        public bool TryGetVoice(string voiceId, [NotNullWhen(true)] out VoiceCatalogEntry? entry)
        {
            entry = null;
            return false;
        }
    }
}
