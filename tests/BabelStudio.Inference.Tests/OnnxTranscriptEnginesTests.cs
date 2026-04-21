using System.Buffers.Binary;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx;
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
