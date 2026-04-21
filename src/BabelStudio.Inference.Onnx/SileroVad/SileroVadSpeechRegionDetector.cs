using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx.Audio;
using BabelStudio.Inference.Runtime.Planning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.SileroVad;

public sealed class SileroVadSpeechRegionDetector : ISpeechRegionDetector, IStageRuntimeExecutionReporter
{
    private const int TargetSampleRate = 16000;
    private const int ChunkSize = 512;
    private const float SpeechThreshold = 0.5f;
    private const float SilenceThreshold = 0.35f;
    private const double MinSpeechDurationSeconds = 0.25;
    private const double MinSilenceDurationSeconds = 0.10;
    private const double SpeechPaddingSeconds = 0.03;
    private readonly IRuntimePlanner runtimePlanner;
    private readonly BenchmarkModelPathResolver modelPathResolver;

    public SileroVadSpeechRegionDetector(
        IRuntimePlanner runtimePlanner,
        BenchmarkModelPathResolver modelPathResolver)
    {
        this.runtimePlanner = runtimePlanner ?? throw new ArgumentNullException(nameof(runtimePlanner));
        this.modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
    }

    public StageRuntimeExecutionSummary? LastExecutionSummary { get; private set; }

    public async Task<IReadOnlyList<SpeechRegion>> DetectAsync(
        string normalizedAudioPath,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        StageRuntimePlan plan = await runtimePlanner.PlanAsync(
            new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: false),
            cancellationToken).ConfigureAwait(false);
        EnsurePlanReady(plan, RuntimeStage.Vad);

        string modelPath = ResolvePlannedModelPath(plan);
        using OnnxExecutionSessionFactory.SingleSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateSingleAsync(modelPath, plan.ExecutionProvider!.Value, cancellationToken)
            .ConfigureAwait(false);

        AudioSamples audio = await WaveAudioReader.ReadMonoPcm16Async(normalizedAudioPath, cancellationToken).ConfigureAwait(false);
        float[] samples = AudioResampler.Resample(audio.Samples, audio.SampleRate, TargetSampleRate);
        float[] paddedSamples = PadToChunkSize(samples, ChunkSize);
        float[] state = new float[2 * 128];
        var probabilities = new List<float>(Math.Max(1, paddedSamples.Length / ChunkSize));

        for (int offset = 0; offset < paddedSamples.Length; offset += ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float[] chunk = new float[ChunkSize];
            Array.Copy(paddedSamples, offset, chunk, 0, ChunkSize);

            using var input = CreateInputSet(chunk, state);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output = sessionLease.Session.Run(input.Values);
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputCollection = output;

            Tensor<float> probabilityTensor = outputCollection.Single(static value => value.Name == "output").AsTensor<float>();
            Tensor<float> stateTensor = outputCollection.Single(static value => value.Name == "stateN").AsTensor<float>();
            probabilities.Add(probabilityTensor[0, 0]);
            CopyState(stateTensor, state);
        }

        LastExecutionSummary = CreateExecutionSummary(plan, sessionLease);
        return BuildSpeechRegions(probabilities, durationSeconds);
    }

    private static InputSet CreateInputSet(float[] chunk, float[] state)
    {
        IReadOnlyList<NamedOnnxValue> values =
        [
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(chunk, [1, ChunkSize])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state.ToArray(), [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { TargetSampleRate }, [1]))
        ];

        return new InputSet(values);
    }

    private static void CopyState(Tensor<float> tensor, float[] destination)
    {
        int index = 0;
        foreach (float value in tensor)
        {
            destination[index++] = value;
        }
    }

    private static float[] PadToChunkSize(float[] samples, int chunkSize)
    {
        if (samples.Length == 0)
        {
            return new float[chunkSize];
        }

        int paddedLength = ((samples.Length + chunkSize - 1) / chunkSize) * chunkSize;
        if (paddedLength == samples.Length)
        {
            return samples;
        }

        var padded = new float[paddedLength];
        Array.Copy(samples, padded, samples.Length);
        return padded;
    }

    private static IReadOnlyList<SpeechRegion> BuildSpeechRegions(
        IReadOnlyList<float> probabilities,
        double durationSeconds)
    {
        if (probabilities.Count == 0 || durationSeconds <= 0)
        {
            return [];
        }

        double secondsPerChunk = ChunkSize / (double)TargetSampleRate;
        double minSpeechDuration = MinSpeechDurationSeconds;
        double minSilenceDuration = MinSilenceDurationSeconds;
        double speechPadding = SpeechPaddingSeconds;
        var rawRegions = new List<(double Start, double End)>();
        bool inSpeech = false;
        int? speechStartChunk = null;
        int lastSpeechChunk = -1;
        int pendingSilenceChunks = 0;

        for (int index = 0; index < probabilities.Count; index++)
        {
            float probability = probabilities[index];
            if (probability >= SpeechThreshold)
            {
                if (!inSpeech)
                {
                    inSpeech = true;
                    speechStartChunk = index;
                }

                lastSpeechChunk = index;
                pendingSilenceChunks = 0;
                continue;
            }

            if (!inSpeech)
            {
                continue;
            }

            if (probability < SilenceThreshold)
            {
                pendingSilenceChunks++;
            }

            if (pendingSilenceChunks * secondsPerChunk < minSilenceDuration)
            {
                continue;
            }

            AppendRegion(rawRegions, speechStartChunk!.Value, lastSpeechChunk, secondsPerChunk, minSpeechDuration, durationSeconds, speechPadding);
            inSpeech = false;
            speechStartChunk = null;
            lastSpeechChunk = -1;
            pendingSilenceChunks = 0;
        }

        if (inSpeech && speechStartChunk is not null)
        {
            AppendRegion(rawRegions, speechStartChunk.Value, lastSpeechChunk, secondsPerChunk, minSpeechDuration, durationSeconds, speechPadding);
        }

        return rawRegions
            .Select((region, index) => new SpeechRegion(index, region.Start, region.End))
            .ToArray();
    }

    private static void AppendRegion(
        ICollection<(double Start, double End)> regions,
        int startChunk,
        int endChunk,
        double secondsPerChunk,
        double minSpeechDuration,
        double durationSeconds,
        double speechPadding)
    {
        double start = Math.Max(0, (startChunk * secondsPerChunk) - speechPadding);
        double end = Math.Min(durationSeconds, ((endChunk + 1) * secondsPerChunk) + speechPadding);
        if (end - start < minSpeechDuration)
        {
            return;
        }

        if (regions.Count > 0)
        {
            (double Start, double End) previous = regions.Last();
            if (start <= previous.End)
            {
                regions.Remove(previous);
                regions.Add((previous.Start, Math.Max(previous.End, end)));
                return;
            }
        }

        regions.Add((start, end));
    }

    private static void EnsurePlanReady(StageRuntimePlan plan, RuntimeStage stage)
    {
        if (plan.Status is StageRuntimePlanStatus.Ready && plan.ExecutionProvider is not null && !string.IsNullOrWhiteSpace(plan.ModelAlias))
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

    private static StageRuntimeExecutionSummary CreateExecutionSummary(
        StageRuntimePlan plan,
        OnnxExecutionSessionFactory.SingleSessionLease sessionLease) =>
        new(
            sessionLease.RequestedProvider,
            sessionLease.SelectedProvider,
            plan.ModelId,
            plan.ModelAlias,
            plan.Variant,
            sessionLease.BootstrapDetail);

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
}
