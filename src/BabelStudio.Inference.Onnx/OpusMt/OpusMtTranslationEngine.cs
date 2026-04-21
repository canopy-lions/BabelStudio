using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.Planning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.OpusMt;

public sealed class OpusMtTranslationEngine : ITranslationEngine, IStageRuntimeExecutionReporter
{
    private readonly IRuntimePlanner runtimePlanner;
    private readonly BenchmarkModelPathResolver modelPathResolver;

    public OpusMtTranslationEngine(
        IRuntimePlanner runtimePlanner,
        BenchmarkModelPathResolver modelPathResolver)
    {
        this.runtimePlanner = runtimePlanner ?? throw new ArgumentNullException(nameof(runtimePlanner));
        this.modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
    }

    public StageRuntimeExecutionSummary? LastExecutionSummary { get; private set; }

    public async Task<IReadOnlyList<TranslatedTextSegment>> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Segments);

        StageRuntimePlan plan = await runtimePlanner.PlanAsync(
            new StageRuntimePlanningRequest(
                RuntimeStage.Translation,
                request.CommercialSafeMode,
                request.PreferredModelAlias,
                request.SourceLanguage,
                request.TargetLanguage),
            cancellationToken).ConfigureAwait(false);
        EnsurePlanReady(plan, RuntimeStage.Translation);

        string encoderModelPath = ResolveEncoderModelPath(plan);
        string decoderModelPath = ResolveDecoderModelPath(plan, encoderModelPath);
        string modelRootPath = ResolveModelRootPath(encoderModelPath);
        OpusTokenizerDecoder tokenizer = OpusTokenizerDecoder.Load(modelRootPath);

        if (request.Segments.Count == 0)
        {
            LastExecutionSummary = CreatePlannedOnlySummary(plan, "Translation skipped because the transcript did not contain any segments.");
            return [];
        }

        using OnnxExecutionSessionFactory.OpusSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateOpusAsync(encoderModelPath, decoderModelPath, plan.ExecutionProvider!.Value, cancellationToken)
            .ConfigureAwait(false);

        var translatedSegments = new List<TranslatedTextSegment>(request.Segments.Count);
        foreach (TranslationInputSegment segment in request.Segments.OrderBy(static segment => segment.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string translatedText = await TranslateSegmentAsync(
                sessionLease,
                tokenizer,
                segment.Text,
                cancellationToken).ConfigureAwait(false);

            translatedSegments.Add(new TranslatedTextSegment(
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                translatedText));
        }

        LastExecutionSummary = CreateExecutionSummary(plan, sessionLease);
        return translatedSegments;
    }

    private static async Task<string> TranslateSegmentAsync(
        OnnxExecutionSessionFactory.OpusSessionLease sessionLease,
        OpusTokenizerDecoder tokenizer,
        string text,
        CancellationToken cancellationToken)
    {
        long[] inputIds = tokenizer.EncodeSourceText(text);
        long[] attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();

        using var encoderInputs = CreateEncoderInputs(
            sessionLease.EncoderSession.InputMetadata,
            inputIds,
            attentionMask);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encoderResults =
            sessionLease.EncoderSession.Run(encoderInputs.Values);
        Tensor<float> encoderHiddenStates = encoderResults
            .Single(static result => result.Name == "last_hidden_state")
            .AsTensor<float>();

        List<long> generatedTokens = await GreedyDecodeAsync(
            sessionLease.DecoderSession,
            tokenizer,
            encoderHiddenStates,
            attentionMask,
            cancellationToken).ConfigureAwait(false);
        string translatedText = tokenizer.DecodeTargetText(generatedTokens);

        return string.IsNullOrWhiteSpace(translatedText)
            ? text.Trim()
            : translatedText;
    }

    private static Task<List<long>> GreedyDecodeAsync(
        InferenceSession decoderSession,
        OpusTokenizerDecoder tokenizer,
        Tensor<float> encoderHiddenStates,
        IReadOnlyList<long> attentionMask,
        CancellationToken cancellationToken)
    {
        var generatedTokens = new List<long> { tokenizer.DecoderStartTokenId };
        int maxSteps = Math.Max(8, tokenizer.MaxGenerationLength);

        for (int step = 0; step < maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var decoderInputs = CreateDecoderInputs(
                decoderSession.InputMetadata,
                encoderHiddenStates,
                attentionMask,
                generatedTokens);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> decoderResults = decoderSession.Run(decoderInputs.Values);
            Tensor<float> logits = decoderResults
                .Single(static result => result.Name == "logits")
                .AsTensor<float>();

            int sequenceLength = logits.Dimensions[1];
            int vocabularySize = logits.Dimensions[2];
            int nextToken = SelectNextToken(
                logits,
                sequenceLength - 1,
                vocabularySize,
                tokenizer.PadTokenId);
            if (nextToken == tokenizer.EndOfSentenceTokenId || nextToken < 0)
            {
                break;
            }

            generatedTokens.Add(nextToken);
        }

        return Task.FromResult(generatedTokens.Skip(1).ToList());
    }

    private static int SelectNextToken(
        Tensor<float> logits,
        int timeIndex,
        int vocabularySize,
        int padTokenId)
    {
        int bestToken = -1;
        float bestValue = float.NegativeInfinity;
        for (int tokenIndex = 0; tokenIndex < vocabularySize; tokenIndex++)
        {
            if (tokenIndex == padTokenId)
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

    private static InputSet CreateEncoderInputs(
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata,
        IReadOnlyList<long> inputIds,
        IReadOnlyList<long> attentionMask)
    {
        var values = new List<NamedOnnxValue>(inputMetadata.Count);
        foreach ((string inputName, _) in inputMetadata)
        {
            values.Add(inputName switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor(
                    "input_ids",
                    new DenseTensor<long>(inputIds.ToArray(), [1, inputIds.Count])),
                "attention_mask" => NamedOnnxValue.CreateFromTensor(
                    "attention_mask",
                    new DenseTensor<long>(attentionMask.ToArray(), [1, attentionMask.Count])),
                _ => throw new NotSupportedException($"Opus encoder input '{inputName}' is not supported.")
            });
        }

        return new InputSet(values);
    }

    private static InputSet CreateDecoderInputs(
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata,
        Tensor<float> encoderHiddenStates,
        IReadOnlyList<long> attentionMask,
        IReadOnlyList<long> generatedTokens)
    {
        var values = new List<NamedOnnxValue>(inputMetadata.Count);
        DenseTensor<long> inputIdsTensor = new(generatedTokens.ToArray(), [1, generatedTokens.Count]);
        DenseTensor<long> attentionMaskTensor = new(attentionMask.ToArray(), [1, attentionMask.Count]);

        foreach ((string inputName, _) in inputMetadata)
        {
            values.Add(inputName switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                "encoder_hidden_states" => NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                "attention_mask" => NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                "encoder_attention_mask" => NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attentionMaskTensor),
                "use_cache_branch" => NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { false }, new[] { 1 })),
                _ when inputName.StartsWith("past_key_values.", StringComparison.Ordinal) =>
                    NamedOnnxValue.CreateFromTensor(inputName, CreateEmptyPastTensor()),
                _ => throw new NotSupportedException($"Opus decoder input '{inputName}' is not supported.")
            });
        }

        return new InputSet(values);
    }

    private static DenseTensor<float> CreateEmptyPastTensor() =>
        new(Array.Empty<float>(), new[] { 1, 8, 0, 64 });

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

    private string ResolveEncoderModelPath(StageRuntimePlan plan)
    {
        BenchmarkModelCandidate candidate = modelPathResolver.ResolveSingle(plan.ModelAlias!);
        return candidate.ModelPath;
    }

    private string ResolveDecoderModelPath(StageRuntimePlan plan, string encoderModelPath)
    {
        if (!string.IsNullOrWhiteSpace(plan.Variant))
        {
            BenchmarkModelCandidate candidate = modelPathResolver.ResolveSingle(plan.ModelAlias!, plan.Variant);
            string fileName = Path.GetFileName(candidate.ModelPath);
            if (fileName.StartsWith("decoder_model", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.ModelPath;
            }
        }

        string decoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model.onnx");
        if (File.Exists(decoderModelPath))
        {
            return Path.GetFullPath(decoderModelPath);
        }

        string mergedDecoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model_merged.onnx");
        if (File.Exists(mergedDecoderModelPath))
        {
            return Path.GetFullPath(mergedDecoderModelPath);
        }

        throw new FileNotFoundException("The Opus decoder model was not found next to the encoder model.", decoderModelPath);
    }

    private static string ResolveModelRootPath(string encoderModelPath)
    {
        string? onnxDirectory = Path.GetDirectoryName(encoderModelPath);
        return onnxDirectory is null
            ? throw new InvalidOperationException("The Opus model root path could not be resolved.")
            : onnxDirectory;
    }

    private static StageRuntimeExecutionSummary CreateExecutionSummary(
        StageRuntimePlan plan,
        OnnxExecutionSessionFactory.OpusSessionLease sessionLease) =>
        new(
            sessionLease.RequestedProvider,
            sessionLease.SelectedProvider,
            plan.ModelId,
            plan.ModelAlias,
            plan.Variant,
            sessionLease.BootstrapDetail);

    private static StageRuntimeExecutionSummary CreatePlannedOnlySummary(
        StageRuntimePlan plan,
        string bootstrapDetail) =>
        new(
            "auto",
            plan.ExecutionProvider is ExecutionProviderKind.DirectMl ? "dml" : "cpu",
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
