using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.Planning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.Kokoro;

public sealed class KokoroTtsEngine : ITtsEngine, IStageRuntimeExecutionReporter
{
    private const string ModelAlias = "kokoro-v1.0";
    private const int SampleRate = 24_000;

    private readonly IRuntimePlanner runtimePlanner;
    private readonly BenchmarkModelPathResolver modelPathResolver;
    private readonly IGraphemeToPhoneme phonemizer;

    public KokoroTtsEngine(
        IRuntimePlanner runtimePlanner,
        BenchmarkModelPathResolver modelPathResolver,
        IGraphemeToPhoneme phonemizer)
    {
        this.runtimePlanner = runtimePlanner ?? throw new ArgumentNullException(nameof(runtimePlanner));
        this.modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
        this.phonemizer = phonemizer ?? throw new ArgumentNullException(nameof(phonemizer));
    }

    public StageRuntimeExecutionSummary? LastExecutionSummary { get; private set; }

    public async Task<TtsSynthesisResult> SynthesizeAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        StageRuntimePlan plan = await runtimePlanner.PlanAsync(
            new StageRuntimePlanningRequest(
                RuntimeStage.Tts,
                CommercialSafeMode: false,
                PreferredModelAlias: ModelAlias,
                SourceLanguage: request.LanguageCode),
            cancellationToken).ConfigureAwait(false);
        EnsurePlanReady(plan);

        BenchmarkModelCandidate candidate = modelPathResolver.ResolveSingle(plan.ModelAlias!, plan.Variant);
        // Prefer the manifest-declared model root (which contains tokenizer.json and voices/).
        // Fall back to the model file's parent directory only when the resolver couldn't
        // determine a root (e.g. user supplied a bare .onnx path outside the bundled layout).
        string modelRootPath = candidate.RootDirectory
            ?? Path.GetDirectoryName(candidate.ModelPath)
            ?? throw new InvalidOperationException("Cannot resolve Kokoro model root path.");

        KokoroTokenizer tokenizer = KokoroTokenizer.Load(modelRootPath);
        KokoroVoiceCatalog voiceCatalog = KokoroVoiceCatalog.Load(modelRootPath);

        string phonemes = string.IsNullOrWhiteSpace(request.PhonemeOverride)
            ? phonemizer.Phonemize(request.Text, request.LanguageCode)
            : request.PhonemeOverride;

        long[] inputIds = tokenizer.Encode(phonemes);

        string binPath = voiceCatalog.GetBinPath(request.Voice.VoiceId)
            ?? throw new FileNotFoundException(
                $"Voicepack '{request.Voice.VoiceId}' not found under '{modelRootPath}/voices/'.",
                request.Voice.VoiceId);

        float[] styleVector = KokoroVoicepackLoader.LoadStyleVector(binPath, inputIds.Length);

        using OnnxExecutionSessionFactory.SingleSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateSingleAsync(candidate.ModelPath, plan.ExecutionProvider!.Value, cancellationToken)
            .ConfigureAwait(false);

        float[] audioSamples = RunInference(sessionLease.Session, inputIds, styleVector, request.Speed);
        byte[] wavBytes = KokoroPcmConverter.EncodePcm16Wav(audioSamples, SampleRate);

        LastExecutionSummary = new StageRuntimeExecutionSummary(
            sessionLease.RequestedProvider,
            sessionLease.SelectedProvider,
            plan.ModelId,
            plan.ModelAlias,
            plan.Variant,
            sessionLease.BootstrapDetail);

        return new TtsSynthesisResult(
            wavBytes,
            DurationSamples: audioSamples.Length,
            SampleRate: SampleRate,
            ModelId: plan.ModelId ?? ModelAlias,
            VoiceId: request.Voice.VoiceId,
            Provider: sessionLease.SelectedProvider);
    }

    private static float[] RunInference(
        InferenceSession session,
        long[] inputIds,
        float[] styleVector,
        float speed)
    {
        var inputs = new List<NamedOnnxValue>(3);
        foreach ((string name, _) in session.InputMetadata)
        {
            inputs.Add(name switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor(
                    "input_ids",
                    new DenseTensor<long>(inputIds, [1, inputIds.Length])),
                "style" => NamedOnnxValue.CreateFromTensor(
                    "style",
                    new DenseTensor<float>(styleVector, [1, 256])),
                "speed" => NamedOnnxValue.CreateFromTensor(
                    "speed",
                    new DenseTensor<float>(new[] { speed }, new[] { 1 })),
                _ => throw new NotSupportedException($"Kokoro input '{name}' is not supported.")
            });
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);

        // Model has one output (waveform/audio); take the first regardless of name.
        DisposableNamedOnnxValue audioOutput = results.Count == 1
            ? results.Single()
            : results.FirstOrDefault(static r => r.Name is "audio" or "waveform")
              ?? throw new InvalidOperationException(
                  $"Kokoro output not found. Available: {string.Join(", ", results.Select(static r => r.Name))}");

        Tensor<float> audioTensor = audioOutput.AsTensor<float>();
        return audioTensor.ToArray();
    }

    private static void EnsurePlanReady(StageRuntimePlan plan)
    {
        if (plan.Status is StageRuntimePlanStatus.Ready &&
            plan.ExecutionProvider is not null &&
            !string.IsNullOrWhiteSpace(plan.ModelAlias))
        {
            return;
        }

        throw new InvalidOperationException(
            plan.Fallback?.Detail ?? "Runtime planner did not produce a ready TTS plan.");
    }
}
