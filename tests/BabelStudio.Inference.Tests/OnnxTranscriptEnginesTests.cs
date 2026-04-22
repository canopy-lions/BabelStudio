using System.Buffers.Binary;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx;
using BabelStudio.Inference.Onnx.Madlad;
using BabelStudio.Inference.Onnx.OpusMt;
using BabelStudio.Inference.Onnx.SileroVad;
using BabelStudio.Inference.Onnx.Whisper;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Inference.Tests;

public sealed class OnnxTranscriptEnginesTests
{
    [Fact]
    public async Task SileroVadSpeechRegionDetector_RunsBundledModelOnSilence()
    {
        string wavePath = CreateSilenceWaveFile(durationSeconds: 1.0);
        try
        {
            var detector = new SileroVadSpeechRegionDetector(
                new StubRuntimePlanner(new StageRuntimePlan
                {
                    Stage = RuntimeStage.Vad,
                    Status = StageRuntimePlanStatus.Ready,
                    ModelId = "onnx-community/silero-vad",
                    ModelAlias = "silero-vad",
                    Variant = "default",
                    ExecutionProvider = ExecutionProviderKind.Cpu
                }),
                BenchmarkModelPathResolver.CreateDefault());

            IReadOnlyList<SpeechRegion> regions = await detector.DetectAsync(wavePath, 1.0, CancellationToken.None);

            Assert.NotNull(regions);
            Assert.NotNull(detector.LastExecutionSummary);
            Assert.Equal("cpu", detector.LastExecutionSummary!.SelectedProvider);
        }
        finally
        {
            if (File.Exists(wavePath))
            {
                File.Delete(wavePath);
            }
        }
    }

    [Fact]
    public async Task WhisperOnnxAudioTranscriptionEngine_RunsBundledModelOnSilence()
    {
        string wavePath = CreateSilenceWaveFile(durationSeconds: 1.0);
        try
        {
            var engine = new WhisperOnnxAudioTranscriptionEngine(
                new StubRuntimePlanner(new StageRuntimePlan
                {
                    Stage = RuntimeStage.Asr,
                    Status = StageRuntimePlanStatus.Ready,
                    ModelId = "onnx-community/whisper-tiny",
                    ModelAlias = "whisper-tiny-onnx",
                    Variant = "default",
                    ExecutionProvider = ExecutionProviderKind.Cpu
                }),
                BenchmarkModelPathResolver.CreateDefault());

            IReadOnlyList<RecognizedTranscriptSegment> segments = await engine.TranscribeAsync(
                wavePath,
                [ new SpeechRegion(0, 0.0, 0.8) ],
                CancellationToken.None);

            RecognizedTranscriptSegment segment = Assert.Single(segments);
            Assert.Equal(0, segment.Index);
            Assert.NotNull(engine.LastExecutionSummary);
            Assert.Equal("cpu", engine.LastExecutionSummary!.SelectedProvider);
        }
        finally
        {
            if (File.Exists(wavePath))
            {
                File.Delete(wavePath);
            }
        }
    }

    [Fact]
    public async Task OpusMtTranslationEngine_TranslatesBundledEnglishToSpanishSentence()
    {
        var engine = new OpusMtTranslationEngine(
            new StubRuntimePlanner(new StageRuntimePlan
            {
                Stage = RuntimeStage.Translation,
                Status = StageRuntimePlanStatus.Ready,
                ModelId = "Helsinki-NLP/opus-mt-en-es",
                ModelAlias = "opus-en-es",
                Variant = "merged-decoder",
                ExecutionProvider = ExecutionProviderKind.Cpu
            }),
            BenchmarkModelPathResolver.CreateDefault());

        IReadOnlyList<TranslatedTextSegment> segments = await engine.TranslateAsync(
            new TranslationRequest(
                "en",
                "es",
                [ new TranslationInputSegment(0, 0.0, 1.0, "Hello, I am Brenna Romaniello, your Spanish teacher from Ole Spanish.") ],
                CommercialSafeMode: true,
                PreferredModelAlias: "opus-en-es"),
            CancellationToken.None);

        TranslatedTextSegment segment = Assert.Single(segments);
        Assert.Equal(0, segment.Index);
        Assert.Equal("Hola, soy Brenna Romaniello, tu profesora de español de Ole Spanish.", segment.Text);
        Assert.NotNull(engine.LastExecutionSummary);
        Assert.Equal("cpu", engine.LastExecutionSummary!.SelectedProvider);
        Assert.Equal("opus-en-es", engine.LastExecutionSummary.ModelAlias);
        Assert.Equal("merged-decoder", engine.LastExecutionSummary.ModelVariant);
    }

    [Fact]
    public async Task OpusMtTranslationEngine_TranslatesBundledSpanishToEnglishSentence()
    {
        var engine = new OpusMtTranslationEngine(
            new StubRuntimePlanner(new StageRuntimePlan
            {
                Stage = RuntimeStage.Translation,
                Status = StageRuntimePlanStatus.Ready,
                ModelId = "onnx-community/opus-mt-es-en",
                ModelAlias = "opus-es-en",
                Variant = "merged-decoder",
                ExecutionProvider = ExecutionProviderKind.Cpu
            }),
            BenchmarkModelPathResolver.CreateDefault());

        IReadOnlyList<TranslatedTextSegment> segments = await engine.TranslateAsync(
            new TranslationRequest(
                "es",
                "en",
                [ new TranslationInputSegment(0, 0.0, 1.0, "Hola, soy Brenna Romaniello, tu profesora de español de Ole Spanish.") ],
                CommercialSafeMode: true,
                PreferredModelAlias: "opus-es-en"),
            CancellationToken.None);

        TranslatedTextSegment segment = Assert.Single(segments);
        Assert.Equal(0, segment.Index);
        Assert.Equal("Hi, I'm Brenna Romaniello, your Spanish teacher at Ole Spanish.", segment.Text);
        Assert.NotNull(engine.LastExecutionSummary);
        Assert.Equal("cpu", engine.LastExecutionSummary!.SelectedProvider);
        Assert.Equal("opus-es-en", engine.LastExecutionSummary.ModelAlias);
        Assert.Equal("merged-decoder", engine.LastExecutionSummary.ModelVariant);
    }

    [FixtureFact("BABELSTUDIO_OPUS_FIXTURE_ROOT", "encoder_model.onnx")]
    [Trait("Category", "Integration")]
    public async Task OpusMtTranslationEngine_UsesFixtureModelWhenProvided()
    {
        string fixtureRoot = RequireFixtureRoot("BABELSTUDIO_OPUS_FIXTURE_ROOT");
        string encoderModelPath = RequireFixtureFile(fixtureRoot, "encoder_model.onnx");

        var engine = new OpusMtTranslationEngine(
            new StubRuntimePlanner(new StageRuntimePlan
            {
                Stage = RuntimeStage.Translation,
                Status = StageRuntimePlanStatus.Ready,
                ModelId = "fixture/opus-mt",
                ModelAlias = "fixture-opus-mt",
                Variant = "merged-decoder",
                ExecutionProvider = ExecutionProviderKind.Cpu
            }),
            BenchmarkModelPathResolver.CreateDefault());

        IReadOnlyList<TranslatedTextSegment> segments = await engine.TranslateAsync(
            new TranslationRequest(
                "en",
                "es",
                [ new TranslationInputSegment(0, 0.0, 1.0, "Hello world.") ],
                CommercialSafeMode: false,
                PreferredModelAlias: "fixture-opus-mt",
                ResolvedModelEntryPath: encoderModelPath),
            CancellationToken.None);

        TranslatedTextSegment segment = Assert.Single(segments);
        Assert.False(string.IsNullOrWhiteSpace(segment.Text));
        Assert.NotNull(engine.LastExecutionSummary);
        Assert.Equal("cpu", engine.LastExecutionSummary!.SelectedProvider);
    }

    [FixtureFact("BABELSTUDIO_MADLAD_FIXTURE_ROOT", "encoder_model_int8.onnx")]
    [Trait("Category", "Integration")]
    public async Task MadladTranslationEngine_UsesFixtureModelWhenProvided()
    {
        string fixtureRoot = RequireFixtureRoot("BABELSTUDIO_MADLAD_FIXTURE_ROOT");
        string encoderModelPath = RequireFixtureFile(fixtureRoot, "encoder_model_int8.onnx");

        var engine = new MadladTranslationEngine(
            new StubRuntimePlanner(new StageRuntimePlan
            {
                Stage = RuntimeStage.Translation,
                Status = StageRuntimePlanStatus.Ready,
                ModelId = "fixture/madlad400",
                ModelAlias = "fixture-madlad400",
                Variant = "int8",
                ExecutionProvider = ExecutionProviderKind.Cpu
            }),
            BenchmarkModelPathResolver.CreateDefault());

        IReadOnlyList<TranslatedTextSegment> segments = await engine.TranslateAsync(
            new TranslationRequest(
                "en",
                "fr",
                [ new TranslationInputSegment(0, 0.0, 1.0, "Hello world.") ],
                CommercialSafeMode: false,
                PreferredModelAlias: "fixture-madlad400",
                ResolvedModelEntryPath: encoderModelPath),
            CancellationToken.None);

        TranslatedTextSegment segment = Assert.Single(segments);
        Assert.False(string.IsNullOrWhiteSpace(segment.Text));
        Assert.NotNull(engine.LastExecutionSummary);
        Assert.Equal("cpu", engine.LastExecutionSummary!.SelectedProvider);
    }

    private static string CreateSilenceWaveFile(double durationSeconds)
    {
        const int sampleRate = 48000;
        const short channelCount = 1;
        const short bitsPerSample = 16;
        int sampleCount = Math.Max(1, (int)Math.Round(durationSeconds * sampleRate));
        int blockAlign = channelCount * (bitsPerSample / 8);
        int dataLength = sampleCount * blockAlign;
        string path = Path.Combine(Path.GetTempPath(), $"babelstudio-onnx-silence-{Guid.NewGuid():N}.wav");

        byte[] buffer = new byte[44 + dataLength];
        WriteAscii(buffer, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 36 + dataLength);
        WriteAscii(buffer, 8, "WAVE");
        WriteAscii(buffer, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(22, 2), channelCount);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28, 4), sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(34, 2), bitsPerSample);
        WriteAscii(buffer, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(40, 4), dataLength);

        File.WriteAllBytes(path, buffer);
        return path;
    }

    private static void WriteAscii(byte[] buffer, int offset, string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            buffer[offset + index] = (byte)text[index];
        }
    }

    private static string RequireFixtureRoot(string environmentVariableName)
    {
        string? fixtureRoot = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            throw new InvalidOperationException($"Set {environmentVariableName} to a fixture model directory to run this integration test.");
        }

        return fixtureRoot;
    }

    private static string RequireFixtureFile(string fixtureRoot, string relativePath)
    {
        string fullPath = Path.Combine(fixtureRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Fixture file '{relativePath}' was not found under '{fixtureRoot}'.", fullPath);
        }

        return fullPath;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class FixtureFactAttribute : FactAttribute
    {
        public FixtureFactAttribute(string environmentVariableName, string requiredRelativePath)
        {
            string? fixtureRoot = Environment.GetEnvironmentVariable(environmentVariableName);
            if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
            {
                Skip = $"Set {environmentVariableName} to a fixture model directory to run this integration test.";
                return;
            }

            string requiredPath = Path.Combine(fixtureRoot, requiredRelativePath);
            if (!File.Exists(requiredPath))
            {
                Skip = $"Fixture file '{requiredRelativePath}' was not found under '{fixtureRoot}'.";
            }
        }
    }

    private sealed class StubRuntimePlanner : IRuntimePlanner
    {
        private readonly StageRuntimePlan plan;

        public StubRuntimePlanner(StageRuntimePlan plan)
        {
            this.plan = plan;
        }

        public Task<StageRuntimePlan> PlanAsync(
            StageRuntimePlanningRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(plan with
            {
                Stage = request.Stage
            });
    }
}
