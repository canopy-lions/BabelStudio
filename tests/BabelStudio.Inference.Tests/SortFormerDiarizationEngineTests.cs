using System.Buffers.Binary;
using BabelStudio.Composition.Runtime.Planning;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Inference.Onnx;
using BabelStudio.Inference.Onnx.Runtime.Planning;
using BabelStudio.Inference.Onnx.SortFormer;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Inference.Tests;

public sealed class SortFormerDiarizationEngineTests
{
    [SortFormerFixtureFact]
    public async Task DiarizeAsync_with_real_sortformer_fixture_produces_valid_turns()
    {
        BundledModelManifestRegistry registry = LoadRegistry();
        _ = ResolveFixtureModelPath(registry)
            ?? throw new InvalidOperationException("SortFormer ONNX fixture resolution unexpectedly failed after attribute validation.");

        string tempDirectory = Path.Combine(Path.GetTempPath(), "BabelStudio.Inference.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string wavePath = Path.Combine(tempDirectory, "sortformer-fixture.wav");

        try
        {
            await WriteTestWaveAsync(wavePath, durationSeconds: 1.0, sampleRate: 16000, CancellationToken.None);
            var runtimePlanner = new RuntimePlanner(
                registry,
                new CommercialSafeEvaluator(),
                new MachineHardwareProfileProvider(),
                new OnnxExecutionProviderDiscovery(),
                new OnnxExecutionProviderSmokeTester(),
                new CompositeModelCacheInventory(
                    new BundledManifestModelCacheInventory(registry)));
            var engine = new SortFormerDiarizationEngine(
                runtimePlanner,
                new BenchmarkModelPathResolver(registry));

            IReadOnlyList<DiarizedSpeakerTurn> turns = await engine.DiarizeAsync(
                wavePath,
                1.0,
                [ new SpeechRegion(0, 0.0, 1.0) ],
                commercialSafeMode: false,
                CancellationToken.None);

            Assert.NotNull(turns);

            foreach (DiarizedSpeakerTurn turn in turns)
            {
                Assert.True(turn.EndSeconds > turn.StartSeconds, $"Turn end ({turn.EndSeconds}) must be greater than start ({turn.StartSeconds}).");
                Assert.True(turn.EndSeconds <= 1.0, $"Turn end ({turn.EndSeconds}) must not exceed audio duration (1.0).");
                if (turn.Confidence is double confidence)
                {
                    Assert.True(confidence >= 0.0 && confidence <= 1.0, $"Turn confidence ({confidence}) must be between 0.0 and 1.0.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static async Task WriteTestWaveAsync(
        string path,
        double durationSeconds,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        int sampleCount = (int)(durationSeconds * sampleRate);
        short[] samples = new short[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            double t = index / (double)sampleRate;
            samples[index] = (short)(Math.Sin(2d * Math.PI * 220d * t) * short.MaxValue * 0.2d);
        }

        byte[] pcmData = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, pcmData, 0, pcmData.Length);
        byte[] header = new byte[44];
        "RIFF"u8.CopyTo(header.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), 36 + pcmData.Length);
        "WAVE"u8.CopyTo(header.AsSpan(8, 4));
        "fmt "u8.CopyTo(header.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28, 4), sampleRate * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32, 2), sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34, 2), 16);
        "data"u8.CopyTo(header.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40, 4), pcmData.Length);

        await using FileStream stream = File.Create(path);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(pcmData, cancellationToken).ConfigureAwait(false);
    }

    private static BundledModelManifestRegistry LoadRegistry()
    {
        if (!BundledModelManifestRegistry.TryLoadDefault(out BundledModelManifestRegistry? registry, out string? error) ||
            registry is null)
        {
            throw new InvalidOperationException(error ?? "Bundled model manifest was not found.");
        }

        return registry;
    }

    private static string? ResolveFixtureModelPath(BundledModelManifestRegistry registry)
    {
        if (!registry.TryResolve("sortformer-diarizer-4spk-v1", out BundledModelManifestResolution? resolution) ||
            resolution is null ||
            !Directory.Exists(resolution.Entry.RootDirectory) ||
            !File.Exists(resolution.Entry.DefaultBenchmarkEntryPath))
        {
            return null;
        }

        return resolution.Entry.DefaultBenchmarkEntryPath;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class SortFormerFixtureFactAttribute : FactAttribute
    {
        public SortFormerFixtureFactAttribute()
        {
            if (!BundledModelManifestRegistry.TryLoadDefault(out BundledModelManifestRegistry? registry, out _) ||
                registry is null)
            {
                Skip = "Bundled model manifest was not found.";
                return;
            }

            try
            {
                if (ResolveFixtureModelPath(registry) is null)
                {
                    Skip = "SortFormer ONNX fixture is not present in the local models directory.";
                }
            }
            catch (Exception)
            {
                Skip = "SortFormer ONNX fixture is not present in the local models directory.";
            }
        }
    }
}
