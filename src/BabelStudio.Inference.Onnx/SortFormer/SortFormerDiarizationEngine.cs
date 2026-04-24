using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx.Audio;
using BabelStudio.Inference.Runtime.Planning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.SortFormer;

public sealed class SortFormerDiarizationEngine : ISpeakerDiarizationEngine, IStageRuntimeExecutionReporter
{
    private const int TargetSampleRate = 16000;
    private const float SpeakerActiveThreshold = 0.5f;
    private const float OverlapThreshold = 0.5f;
    // Maximum supported speakers for sortformer-diarizer-4spk-v1 model
    private const int MaxSupportedSpeakers = 4;
    private readonly IRuntimePlanner runtimePlanner;
    private readonly BenchmarkModelPathResolver modelPathResolver;

    public SortFormerDiarizationEngine(
        IRuntimePlanner runtimePlanner,
        BenchmarkModelPathResolver modelPathResolver)
    {
        this.runtimePlanner = runtimePlanner ?? throw new ArgumentNullException(nameof(runtimePlanner));
        this.modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
    }

    public StageRuntimeExecutionSummary? LastExecutionSummary { get; private set; }

    public async Task<IReadOnlyList<DiarizedSpeakerTurn>> DiarizeAsync(
        string normalizedAudioPath,
        double durationSeconds,
        IReadOnlyList<SpeechRegion> speechRegions,
        bool commercialSafeMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(speechRegions);

        StageRuntimePlan plan = await runtimePlanner.PlanAsync(
            new StageRuntimePlanningRequest(RuntimeStage.Diarization, CommercialSafeMode: commercialSafeMode),
            cancellationToken).ConfigureAwait(false);
        EnsurePlanReady(plan, RuntimeStage.Diarization);

        string modelPath = ResolvePlannedModelPath(plan);
        using OnnxExecutionSessionFactory.SingleSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateSingleAsync(modelPath, plan.ExecutionProvider!.Value, cancellationToken)
            .ConfigureAwait(false);

        AudioSamples audio = await WaveAudioReader.ReadMonoPcm16Async(normalizedAudioPath, cancellationToken).ConfigureAwait(false);
        float[] samples = AudioResampler.Resample(audio.Samples, audio.SampleRate, TargetSampleRate);
        float[] maskedSamples = ApplySpeechMask(samples, speechRegions);

        using var inputSet = CreateInputSet(sessionLease.Session, maskedSamples);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = sessionLease.Session.Run(inputSet.Values);
        Tensor<float> probabilityTensor = ResolveProbabilityTensor(outputs);
        IReadOnlyList<DiarizedSpeakerTurn> turns = DecodeTurns(probabilityTensor, durationSeconds, plan.ModelAlias);

        LastExecutionSummary = new StageRuntimeExecutionSummary(
            sessionLease.RequestedProvider,
            sessionLease.SelectedProvider,
            plan.ModelId,
            plan.ModelAlias,
            plan.Variant,
            sessionLease.BootstrapDetail);
        return turns;
    }

    private static InputSet CreateInputSet(InferenceSession session, float[] samples)
    {
        IReadOnlyDictionary<string, NodeMetadata> inputs = session.InputMetadata;

        KeyValuePair<string, NodeMetadata> waveformInput = default;
        if (inputs.TryGetValue("waveform", out NodeMetadata? waveformMeta))
        {
            waveformInput = new KeyValuePair<string, NodeMetadata>("waveform", waveformMeta);
        }
        else if (inputs.TryGetValue("audio_signal", out NodeMetadata? audioSignalMeta))
        {
            waveformInput = new KeyValuePair<string, NodeMetadata>("audio_signal", audioSignalMeta);
        }
        else
        {
            KeyValuePair<string, NodeMetadata>[] floatInputs = inputs
                .Where(static candidate => candidate.Value.ElementType == typeof(float))
                .ToArray();
            if (floatInputs.Length == 0)
            {
                throw new InvalidOperationException("SortFormer ONNX export does not expose any float waveform input.");
            }

            if (floatInputs.Length > 1)
            {
                throw new InvalidOperationException($"SortFormer ONNX export has {floatInputs.Length} float inputs; expected exactly one waveform input.");
            }

            waveformInput = floatInputs[0];
        }

        if (string.IsNullOrWhiteSpace(waveformInput.Key))
        {
            throw new InvalidOperationException("SortFormer ONNX export does not expose a float waveform input.");
        }

        string waveformInputName = waveformInput.Key;
        int[] waveformDimensions = waveformInput.Value.Dimensions.ToArray();
        IReadOnlyList<NamedOnnxValue> values =
        [
            NamedOnnxValue.CreateFromTensor(
                waveformInputName,
                new DenseTensor<float>(samples, ResolveWaveformShape(waveformDimensions, samples.Length)))
        ];

        KeyValuePair<string, NodeMetadata> lengthInput = default;
        if (inputs.TryGetValue("length", out NodeMetadata? lengthMeta))
        {
            lengthInput = new KeyValuePair<string, NodeMetadata>("length", lengthMeta);
        }
        else if (inputs.TryGetValue("audio_signal_length", out NodeMetadata? audioLengthMeta))
        {
            lengthInput = new KeyValuePair<string, NodeMetadata>("audio_signal_length", audioLengthMeta);
        }
        else
        {
            lengthInput = inputs.FirstOrDefault(static candidate =>
                candidate.Value.ElementType == typeof(long) &&
                candidate.Key.Contains("length", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(lengthInput.Key))
        {
            values = values
                .Append(NamedOnnxValue.CreateFromTensor(
                    lengthInput.Key,
                    new DenseTensor<long>(new long[] { samples.Length }, [1])))
                .ToArray();
        }

        return new InputSet(values);
    }

    private static int[] ResolveWaveformShape(IReadOnlyList<int> modelDimensions, int sampleCount)
    {
        if (modelDimensions.Count == 1)
        {
            return [ sampleCount ];
        }

        if (modelDimensions.Count == 2)
        {
            return [ 1, sampleCount ];
        }

        throw new InvalidOperationException("SortFormer ONNX export waveform input must be rank 1 or 2.");
    }

    private static Tensor<float> ResolveProbabilityTensor(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
    {
        foreach (DisposableNamedOnnxValue output in outputs)
        {
            try
            {
                Tensor<float> tensor = output.AsTensor<float>();
                int[] dimensions = tensor.Dimensions.ToArray();
                if (dimensions.Length is 2 or 3)
                {
                    return tensor;
                }
            }
            catch (InvalidCastException)
            {
            }
        }

        throw new InvalidOperationException("SortFormer ONNX export did not produce a frame probability tensor.");
    }

    private static IReadOnlyList<DiarizedSpeakerTurn> DecodeTurns(Tensor<float> probabilities, double durationSeconds, string? modelAlias)
    {
        if (durationSeconds <= 0d)
        {
            return [];
        }

        (int frameCount, int speakerCount, Func<int, int, float> accessor) = CreateTensorAccessor(probabilities);
        if (frameCount <= 0 || speakerCount <= 0)
        {
            return [];
        }

        if (speakerCount > MaxSupportedSpeakers)
        {
            throw new InvalidOperationException(
                $"SortFormer model '{modelAlias ?? "unknown"}' produced {speakerCount} speakers, " +
                $"exceeding the maximum supported count of {MaxSupportedSpeakers} (frameCount={frameCount}).");
        }

        double secondsPerFrame = durationSeconds / frameCount;
        if (!double.IsFinite(secondsPerFrame) || secondsPerFrame <= 0d)
        {
            return [];
        }

        var turns = new List<DiarizedSpeakerTurn>();
        ActiveTurn? activeTurn = null;

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            int primarySpeakerIndex = -1;
            float primarySpeakerProbability = float.NegativeInfinity;
            int activeSpeakerCount = 0;

            for (int speakerIndex = 0; speakerIndex < speakerCount; speakerIndex++)
            {
                float probability = accessor(frameIndex, speakerIndex);
                if (probability >= OverlapThreshold)
                {
                    activeSpeakerCount++;
                }

                if (probability > primarySpeakerProbability)
                {
                    primarySpeakerProbability = probability;
                    primarySpeakerIndex = speakerIndex;
                }
            }

            double frameStart = frameIndex * secondsPerFrame;
            double frameEnd = Math.Min(durationSeconds, (frameIndex + 1) * secondsPerFrame);
            bool isSilentFrame = primarySpeakerProbability < SpeakerActiveThreshold || primarySpeakerIndex < 0;
            if (isSilentFrame)
            {
                FlushActiveTurn(turns, ref activeTurn, durationSeconds);
                continue;
            }

            bool hasOverlap = activeSpeakerCount > 1;
            if (activeTurn is not null &&
                activeTurn.SpeakerIndex == primarySpeakerIndex &&
                Math.Abs(activeTurn.EndSeconds - frameStart) <= secondsPerFrame * 1.5d)
            {
                activeTurn = activeTurn with
                {
                    EndSeconds = frameEnd,
                    ConfidenceSum = activeTurn.ConfidenceSum + primarySpeakerProbability,
                    FrameCount = activeTurn.FrameCount + 1,
                    HasOverlap = activeTurn.HasOverlap || hasOverlap
                };
                continue;
            }

            FlushActiveTurn(turns, ref activeTurn, durationSeconds);
            activeTurn = new ActiveTurn(
                primarySpeakerIndex,
                frameStart,
                frameEnd,
                primarySpeakerProbability,
                FrameCount: 1,
                hasOverlap);
        }

        FlushActiveTurn(turns, ref activeTurn, durationSeconds);
        return turns;
    }

    private static (int FrameCount, int SpeakerCount, Func<int, int, float> Accessor) CreateTensorAccessor(Tensor<float> tensor)
    {
        int[] dimensions = tensor.Dimensions.ToArray();
        return dimensions.Length switch
        {
            2 => (dimensions[0], dimensions[1], (frameIndex, speakerIndex) => tensor[frameIndex, speakerIndex]),
            3 when dimensions[0] == 1 => (dimensions[1], dimensions[2], (frameIndex, speakerIndex) => tensor[0, frameIndex, speakerIndex]),
            3 when dimensions[2] == 1 => (dimensions[0], dimensions[1], (frameIndex, speakerIndex) => tensor[frameIndex, speakerIndex, 0]),
            _ => throw new InvalidOperationException("SortFormer ONNX export probability tensor must be rank 2 or batch-first rank 3.")
        };
    }

    private static void FlushActiveTurn(
        ICollection<DiarizedSpeakerTurn> turns,
        ref ActiveTurn? activeTurn,
        double durationSeconds)
    {
        if (activeTurn is null)
        {
            return;
        }

        double clippedStart = Math.Clamp(activeTurn.StartSeconds, 0d, durationSeconds);
        double clippedEnd = Math.Clamp(activeTurn.EndSeconds, clippedStart, durationSeconds);
        if (clippedEnd > clippedStart)
        {
            turns.Add(new DiarizedSpeakerTurn(
                $"spk_{activeTurn.SpeakerIndex}",
                clippedStart,
                clippedEnd,
                activeTurn.ConfidenceSum / activeTurn.FrameCount,
                activeTurn.HasOverlap));
        }

        activeTurn = null;
    }

    private static float[] ApplySpeechMask(float[] samples, IReadOnlyList<SpeechRegion> speechRegions)
    {
        if (speechRegions.Count == 0)
        {
            return samples;
        }

        var masked = new float[samples.Length];
        foreach (SpeechRegion region in speechRegions)
        {
            int start = Math.Max(0, (int)Math.Floor(region.StartSeconds * TargetSampleRate));
            int end = Math.Min(samples.Length, (int)Math.Ceiling(region.EndSeconds * TargetSampleRate));
            if (end <= start)
            {
                continue;
            }

            Array.Copy(samples, start, masked, start, end - start);
        }

        return masked;
    }

    private static void EnsurePlanReady(StageRuntimePlan plan, RuntimeStage stage)
    {
        if (plan.Status is StageRuntimePlanStatus.Ready &&
            plan.ExecutionProvider is not null &&
            !string.IsNullOrWhiteSpace(plan.ModelAlias))
        {
            return;
        }

        throw new InvalidOperationException(
            plan.Fallback?.Detail ??
            $"Runtime planner did not produce a ready {stage} plan.");
    }

    private string ResolvePlannedModelPath(StageRuntimePlan plan)
    {
        BenchmarkModelCandidate candidate = modelPathResolver.ResolveSingle(plan.ModelAlias!, plan.Variant);
        return candidate.ModelPath;
    }

    private sealed class InputSet : IDisposable
    {
        public InputSet(IReadOnlyList<NamedOnnxValue> values)
        {
            Values = values;
        }

        public IReadOnlyList<NamedOnnxValue> Values { get; }

        public void Dispose()
        {
            foreach (IDisposable value in Values.OfType<IDisposable>())
            {
                value.Dispose();
            }
        }
    }

    private sealed record ActiveTurn(
        int SpeakerIndex,
        double StartSeconds,
        double EndSeconds,
        double ConfidenceSum,
        int FrameCount,
        bool HasOverlap);
}
