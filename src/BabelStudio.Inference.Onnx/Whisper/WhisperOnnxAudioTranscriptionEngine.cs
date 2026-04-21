using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx.Audio;
using BabelStudio.Inference.Runtime.Planning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.Whisper;

public sealed class WhisperOnnxAudioTranscriptionEngine : IAudioTranscriptionEngine, IStageRuntimeExecutionReporter
{
    private const double MaxChunkDurationSeconds = 28;
    private readonly IRuntimePlanner runtimePlanner;
    private readonly BenchmarkModelPathResolver modelPathResolver;
    private readonly WhisperFeatureExtractor featureExtractor = new();

    public WhisperOnnxAudioTranscriptionEngine(
        IRuntimePlanner runtimePlanner,
        BenchmarkModelPathResolver modelPathResolver)
    {
        this.runtimePlanner = runtimePlanner ?? throw new ArgumentNullException(nameof(runtimePlanner));
        this.modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
    }

    public StageRuntimeExecutionSummary? LastExecutionSummary { get; private set; }

    public async Task<IReadOnlyList<RecognizedTranscriptSegment>> TranscribeAsync(
        string normalizedAudioPath,
        IReadOnlyList<SpeechRegion> regions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(regions);

        StageRuntimePlan plan = await runtimePlanner.PlanAsync(
            new StageRuntimePlanningRequest(RuntimeStage.Asr, CommercialSafeMode: false),
            cancellationToken).ConfigureAwait(false);
        EnsurePlanReady(plan, RuntimeStage.Asr);

        string encoderModelPath = ResolvePlannedModelPath(plan);
        string decoderModelPath = ResolveWhisperDecoderPath(encoderModelPath);
        string modelRootPath = ResolveModelRootPath(encoderModelPath);
        var tokenizer = WhisperTokenizerDecoder.Load(modelRootPath);

        if (regions.Count == 0)
        {
            LastExecutionSummary = CreatePlannedOnlySummary(plan, "ASR skipped because VAD did not detect any speech regions.");
            return [];
        }

        using OnnxExecutionSessionFactory.WhisperSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateWhisperAsync(encoderModelPath, decoderModelPath, plan.ExecutionProvider!.Value, cancellationToken)
            .ConfigureAwait(false);

        AudioSamples audio = await WaveAudioReader.ReadMonoPcm16Async(normalizedAudioPath, cancellationToken).ConfigureAwait(false);
        float[] samples = WhisperFeatureExtractor.PrepareSamples(audio.Samples, audio.SampleRate);
        var segments = new List<RecognizedTranscriptSegment>(regions.Count);

        foreach (SpeechRegion region in regions.OrderBy(static region => region.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text = await TranscribeRegionAsync(
                sessionLease,
                tokenizer,
                samples,
                region,
                cancellationToken).ConfigureAwait(false);

            segments.Add(new RecognizedTranscriptSegment(
                region.Index,
                region.StartSeconds,
                region.EndSeconds,
                text));
        }

        LastExecutionSummary = CreateExecutionSummary(plan, sessionLease);
        return segments;
    }

    private async Task<string> TranscribeRegionAsync(
        OnnxExecutionSessionFactory.WhisperSessionLease sessionLease,
        WhisperTokenizerDecoder tokenizer,
        float[] samples,
        SpeechRegion region,
        CancellationToken cancellationToken)
    {
        int startSample = Math.Max(0, (int)Math.Floor(region.StartSeconds * 16000d));
        int endSample = Math.Min(samples.Length, (int)Math.Ceiling(region.EndSeconds * 16000d));
        if (endSample <= startSample)
        {
            return string.Empty;
        }

        float[] regionSamples = samples[startSample..endSample];
        List<string> chunkTexts = [];
        int maxChunkSamples = (int)(MaxChunkDurationSeconds * 16000d);

        for (int offset = 0; offset < regionSamples.Length; offset += maxChunkSamples)
        {
            int chunkLength = Math.Min(maxChunkSamples, regionSamples.Length - offset);
            float[] chunkSamples = new float[chunkLength];
            Array.Copy(regionSamples, offset, chunkSamples, 0, chunkLength);

            DenseTensor<float> features = featureExtractor.Extract(chunkSamples);
            using var encoderInputs = new InputSet(
            [
                NamedOnnxValue.CreateFromTensor("input_features", features)
            ]);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encoderResults =
                sessionLease.EncoderSession.Run(encoderInputs.Values);
            Tensor<float> hiddenStates = encoderResults.Single().AsTensor<float>();

            List<int> outputTokens = await GreedyDecodeAsync(
                sessionLease.DecoderSession,
                tokenizer,
                hiddenStates,
                cancellationToken).ConfigureAwait(false);
            string decodedText = tokenizer.DecodeText(outputTokens);
            if (!string.IsNullOrWhiteSpace(decodedText))
            {
                chunkTexts.Add(decodedText);
            }
        }

        return string.Join(" ", chunkTexts).Trim();
    }

    private static Task<List<int>> GreedyDecodeAsync(
        InferenceSession decoderSession,
        WhisperTokenizerDecoder tokenizer,
        Tensor<float> encoderHiddenStates,
        CancellationToken cancellationToken)
    {
        var generated = tokenizer.InitialPromptTokens.Select(static token => (long)token).ToList();
        int maxTokens = 448;

        for (int step = generated.Count; step < maxTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var decoderInputs = CreateDecoderInputs(generated, encoderHiddenStates);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> decoderResults = decoderSession.Run(decoderInputs.Values);
            Tensor<float> logits = decoderResults.Single(static result => result.Name == "logits").AsTensor<float>();

            int sequenceLength = logits.Dimensions[1];
            int vocabularySize = logits.Dimensions[2];
            int nextToken = SelectNextToken(logits, sequenceLength - 1, vocabularySize, tokenizer);
            if (nextToken == tokenizer.EndOfTranscriptToken || nextToken < 0)
            {
                break;
            }

            generated.Add(nextToken);
        }

        return Task.FromResult(generated.Skip(tokenizer.InitialPromptTokens.Count).Select(static token => (int)token).ToList());
    }

    private static int SelectNextToken(
        Tensor<float> logits,
        int timeIndex,
        int vocabularySize,
        WhisperTokenizerDecoder tokenizer)
    {
        int bestToken = -1;
        float bestValue = float.NegativeInfinity;
        for (int tokenIndex = 0; tokenIndex < vocabularySize; tokenIndex++)
        {
            if (tokenIndex >= tokenizer.TimestampBeginToken && tokenIndex != tokenizer.EndOfTranscriptToken)
            {
                continue;
            }

            if (tokenizer.SuppressedTokens.Contains(tokenIndex))
            {
                continue;
            }

            float value = logits[0, timeIndex, tokenIndex];
            if (value > bestValue)
            {
                bestValue = value;
                bestToken = tokenIndex;
            }
        }

        return bestToken;
    }

    private static InputSet CreateDecoderInputs(IReadOnlyList<long> generatedTokens, Tensor<float> encoderHiddenStates)
    {
        long[] tokenArray = generatedTokens.ToArray();
        return new InputSet(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(tokenArray, [1, tokenArray.Length])),
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates)
        ]);
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

    private static string ResolveWhisperDecoderPath(string encoderModelPath)
    {
        string fileName = Path.GetFileName(encoderModelPath);
        string decoderFileName = fileName.Replace("encoder_model", "decoder_model", StringComparison.OrdinalIgnoreCase);
        string decoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, decoderFileName);
        if (File.Exists(decoderModelPath))
        {
            return Path.GetFullPath(decoderModelPath);
        }

        decoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model.onnx");
        if (File.Exists(decoderModelPath))
        {
            return Path.GetFullPath(decoderModelPath);
        }

        throw new FileNotFoundException("Whisper decoder model was not found next to the encoder model.", decoderModelPath);
    }

    private static string ResolveModelRootPath(string encoderModelPath)
    {
        string? onnxDirectory = Path.GetDirectoryName(encoderModelPath);
        string? modelRoot = Path.GetDirectoryName(onnxDirectory);
        return modelRoot ?? throw new InvalidOperationException("Whisper model root path could not be resolved.");
    }

    private static StageRuntimeExecutionSummary CreateExecutionSummary(
        StageRuntimePlan plan,
        OnnxExecutionSessionFactory.WhisperSessionLease sessionLease) =>
        new(
            sessionLease.RequestedProvider,
            sessionLease.SelectedProvider,
            plan.ModelId,
            plan.ModelAlias,
            plan.Variant,
            sessionLease.BootstrapDetail);

    private static StageRuntimeExecutionSummary CreatePlannedOnlySummary(StageRuntimePlan plan, string bootstrapDetail) =>
        new(
            "auto",
            plan.ExecutionProvider!.Value.ToString().ToLowerInvariant(),
            plan.ModelId,
            plan.ModelAlias,
            plan.Variant,
            bootstrapDetail);

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
