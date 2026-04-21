using System.Diagnostics;
using System.Text.Json;
using BabelStudio.Domain;
using BabelStudio.Inference;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Onnx.WindowsMl;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx;

public sealed class OnnxModelBenchmarkRunner : IModelBenchmarkRunner
{
    public async Task<BenchmarkReport> RunAsync(BenchmarkRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullModelPath = request.ModelPath;
        var fullReportPath = Path.GetFullPath(request.ReportPath);
        var requestedProvider = request.ProviderPreference.ToString().ToLowerInvariant();
        var selectedProvider = requestedProvider;
        var modelSizeBytes = File.Exists(fullModelPath) ? new FileInfo(fullModelPath).Length : 0L;
        var notes = new List<string>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            fullModelPath = ResolveModelPath(fullModelPath, notes);
            await TryBootstrapWindowsMlAsync(request.ProviderPreference, notes, cancellationToken).ConfigureAwait(false);

            modelSizeBytes = new FileInfo(fullModelPath).Length;
            notes.Add($"Model file discovered at '{fullModelPath}'.");

            BenchmarkExecution execution = LooksLikeWhisperEncoder(fullModelPath)
                ? RunWhisperExecution(fullModelPath, request.ProviderPreference, request.RunCount, notes)
                : LooksLikeOpusEncoder(fullModelPath)
                    ? RunOpusExecution(fullModelPath, request.ProviderPreference, request.RunCount, notes)
                : RunSingleSessionExecution(fullModelPath, request.ProviderPreference, request.RunCount, notes);

            requestedProvider = execution.RequestedProvider;
            selectedProvider = execution.SelectedProvider;
            modelSizeBytes = execution.ModelSizeBytes;

            var realTimeFactorAverage = CalculateRealTimeFactorAverage(
                execution.AudioDurationSeconds,
                execution.WarmLatencyAverageMilliseconds);

            var measurements = new BenchmarkMeasurements(
                ColdLoadMilliseconds: execution.ColdLoadMilliseconds,
                WarmupMilliseconds: execution.WarmupMilliseconds,
                WarmLatencyAverageMilliseconds: execution.WarmLatencyAverageMilliseconds,
                WarmLatencyMinimumMilliseconds: execution.WarmLatencyMinimumMilliseconds,
                WarmLatencyMaximumMilliseconds: execution.WarmLatencyMaximumMilliseconds,
                AudioDurationSeconds: execution.AudioDurationSeconds,
                RealTimeFactorAverage: realTimeFactorAverage);

            return CreateReport(
                scenario: execution.Scenario,
                status: BenchmarkStatus.Completed,
                modelPath: fullModelPath,
                reportPath: fullReportPath,
                requestedProvider: requestedProvider,
                selectedProvider: selectedProvider,
                runCount: request.RunCount,
                supportsExecution: true,
                modelSizeBytes: modelSizeBytes,
                measurements: measurements,
                failureReason: null,
                notes: notes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            notes.Add($"Benchmark failed: {ex.GetType().Name}");
            return CreateReport(
                scenario: ResolveFailureScenario(fullModelPath),
                status: BenchmarkStatus.Failed,
                modelPath: fullModelPath,
                reportPath: fullReportPath,
                requestedProvider: requestedProvider,
                selectedProvider: selectedProvider,
                runCount: request.RunCount,
                supportsExecution: false,
                modelSizeBytes: modelSizeBytes,
                measurements: EmptyMeasurements(),
                failureReason: ex.Message,
                notes: notes);
        }
    }

    private static async Task TryBootstrapWindowsMlAsync(
        BenchmarkProviderPreference preference,
        ICollection<string> notes,
        CancellationToken cancellationToken)
    {
        if (preference is BenchmarkProviderPreference.Cpu)
        {
            notes.Add("Windows ML bootstrap skipped for CPU-only provider route.");
            return;
        }

        var bootstrapper = new WindowsMlExecutionProviderBootstrapper();
        WindowsMlBootstrapResult result = await bootstrapper
            .EnsureAndRegisterCertifiedAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            notes.Add($"Windows ML bootstrap succeeded via {result.Mode}.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            notes.Add($"Windows ML bootstrap did not complete: {result.FailureReason}");
            return;
        }

        notes.Add("Windows ML bootstrap did not complete.");
    }

    private static BenchmarkExecution RunSingleSessionExecution(
        string modelPath,
        BenchmarkProviderPreference preference,
        int runCount,
        ICollection<string> notes)
    {
        var coldLoadStopwatch = Stopwatch.StartNew();
        using var sessionLease = CreateSession(modelPath, preference, notes);
        coldLoadStopwatch.Stop();

        var inputSet = CreateInputs(modelPath, sessionLease.Session.InputMetadata);

        var warmupStopwatch = Stopwatch.StartNew();
        using (sessionLease.Session.Run(inputSet.Values))
        {
        }
        warmupStopwatch.Stop();

        var latencySamples = new List<double>(runCount);
        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            var measuredStopwatch = Stopwatch.StartNew();
            using var results = sessionLease.Session.Run(inputSet.Values);
            measuredStopwatch.Stop();

            latencySamples.Add(measuredStopwatch.Elapsed.TotalMilliseconds);
        }

        return new BenchmarkExecution(
            Scenario: "onnx-model",
            RequestedProvider: sessionLease.RequestedProvider,
            SelectedProvider: sessionLease.SelectedProvider,
            ModelSizeBytes: new FileInfo(modelPath).Length,
            ColdLoadMilliseconds: coldLoadStopwatch.Elapsed.TotalMilliseconds,
            WarmupMilliseconds: warmupStopwatch.Elapsed.TotalMilliseconds,
            WarmLatencyAverageMilliseconds: latencySamples.Average(),
            WarmLatencyMinimumMilliseconds: latencySamples.Min(),
            WarmLatencyMaximumMilliseconds: latencySamples.Max(),
            AudioDurationSeconds: inputSet.AudioDurationSeconds);
    }

    private static BenchmarkExecution RunWhisperExecution(
        string encoderModelPath,
        BenchmarkProviderPreference preference,
        int runCount,
        ICollection<string> notes)
    {
        var decoderModelPath = ResolveWhisperDecoderPath(encoderModelPath);
        var configPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "..", "config.json");
        var fullConfigPath = Path.GetFullPath(configPath);

        notes.Add($"Whisper decoder discovered at '{decoderModelPath}'.");

        var coldLoadStopwatch = Stopwatch.StartNew();
        using var whisperLease = CreateWhisperSessionLease(encoderModelPath, decoderModelPath, preference, notes);
        coldLoadStopwatch.Stop();

        var encoderInputSet = CreateInputs(encoderModelPath, whisperLease.EncoderSession.InputMetadata);
        var decoderStartTokenId = ResolveWhisperDecoderStartTokenId(fullConfigPath);

        var warmupStopwatch = Stopwatch.StartNew();
        RunWhisperPass(whisperLease, encoderInputSet, decoderStartTokenId);
        warmupStopwatch.Stop();

        var latencySamples = new List<double>(runCount);
        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            var measuredStopwatch = Stopwatch.StartNew();
            RunWhisperPass(whisperLease, encoderInputSet, decoderStartTokenId);
            measuredStopwatch.Stop();

            latencySamples.Add(measuredStopwatch.Elapsed.TotalMilliseconds);
        }

        return new BenchmarkExecution(
            Scenario: "whisper-encoder-decoder",
            RequestedProvider: whisperLease.RequestedProvider,
            SelectedProvider: whisperLease.SelectedProvider,
            ModelSizeBytes: new FileInfo(encoderModelPath).Length + new FileInfo(decoderModelPath).Length,
            ColdLoadMilliseconds: coldLoadStopwatch.Elapsed.TotalMilliseconds,
            WarmupMilliseconds: warmupStopwatch.Elapsed.TotalMilliseconds,
            WarmLatencyAverageMilliseconds: latencySamples.Average(),
            WarmLatencyMinimumMilliseconds: latencySamples.Min(),
            WarmLatencyMaximumMilliseconds: latencySamples.Max(),
            AudioDurationSeconds: encoderInputSet.AudioDurationSeconds);
    }

    private static BenchmarkExecution RunOpusExecution(
        string encoderModelPath,
        BenchmarkProviderPreference preference,
        int runCount,
        ICollection<string> notes)
    {
        var decoderModelPath = ResolveOpusDecoderPath(encoderModelPath);
        var configPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "..", "config.json");
        var fullConfigPath = Path.GetFullPath(configPath);

        notes.Add($"Opus decoder discovered at '{decoderModelPath}'.");

        var coldLoadStopwatch = Stopwatch.StartNew();
        using var opusLease = CreateOpusSessionLease(encoderModelPath, decoderModelPath, preference, notes);
        coldLoadStopwatch.Stop();

        var encoderInputSet = CreateInputs(encoderModelPath, opusLease.EncoderSession.InputMetadata);
        var decoderStartTokenId = ResolveOpusDecoderStartTokenId(fullConfigPath);

        var warmupStopwatch = Stopwatch.StartNew();
        RunOpusPass(opusLease, encoderInputSet, decoderStartTokenId);
        warmupStopwatch.Stop();

        var latencySamples = new List<double>(runCount);
        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            var measuredStopwatch = Stopwatch.StartNew();
            RunOpusPass(opusLease, encoderInputSet, decoderStartTokenId);
            measuredStopwatch.Stop();

            latencySamples.Add(measuredStopwatch.Elapsed.TotalMilliseconds);
        }

        return new BenchmarkExecution(
            Scenario: "opus-mt-encoder-decoder",
            RequestedProvider: opusLease.RequestedProvider,
            SelectedProvider: opusLease.SelectedProvider,
            ModelSizeBytes: new FileInfo(encoderModelPath).Length + new FileInfo(decoderModelPath).Length,
            ColdLoadMilliseconds: coldLoadStopwatch.Elapsed.TotalMilliseconds,
            WarmupMilliseconds: warmupStopwatch.Elapsed.TotalMilliseconds,
            WarmLatencyAverageMilliseconds: latencySamples.Average(),
            WarmLatencyMinimumMilliseconds: latencySamples.Min(),
            WarmLatencyMaximumMilliseconds: latencySamples.Max(),
            AudioDurationSeconds: null);
    }

    private static BenchmarkSessionLease CreateSession(
        string modelPath,
        BenchmarkProviderPreference preference,
        ICollection<string> notes) =>
        preference switch
        {
            BenchmarkProviderPreference.Cpu => CreateCpuSession(modelPath, notes),
            BenchmarkProviderPreference.Dml => CreateDirectMlSession(modelPath, notes),
            BenchmarkProviderPreference.Auto => CreateAutoSession(modelPath, notes),
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown provider preference.")
        };

    private static BenchmarkSessionLease CreateCpuSession(string modelPath, ICollection<string> notes)
    {
        notes.Add("Provider route: explicit CPU execution provider.");
        var session = new InferenceSession(modelPath, CreateSessionOptions());
        return new BenchmarkSessionLease(session, "cpu", "cpu");
    }

    private static BenchmarkSessionLease CreateDirectMlSession(string modelPath, ICollection<string> notes)
    {
        notes.Add("Provider route: explicit DirectML execution provider.");
        var options = CreateSessionOptions();
        options.AppendExecutionProvider_DML();
        var session = new InferenceSession(modelPath, options);
        return new BenchmarkSessionLease(session, "dml", "dml");
    }

    private static BenchmarkSessionLease CreateAutoSession(string modelPath, ICollection<string> notes)
    {
        notes.Add("Provider route: auto selected.");

        try
        {
            var options = CreateSessionOptions();
            options.AppendExecutionProvider_DML();
            var session = new InferenceSession(modelPath, options);
            notes.Add("Auto route resolved to DirectML.");
            return new BenchmarkSessionLease(session, "auto", "dml");
        }
        catch (OnnxRuntimeException ex)
        {
            notes.Add($"Auto route fell back to CPU because DirectML was unavailable: {ex.Message}");
            var session = new InferenceSession(modelPath, CreateSessionOptions());
            return new BenchmarkSessionLease(session, "auto", "cpu");
        }
        catch (EntryPointNotFoundException ex)
        {
            notes.Add($"Auto route fell back to CPU because DirectML entry points were unavailable: {ex.Message}");
            var session = new InferenceSession(modelPath, CreateSessionOptions());
            return new BenchmarkSessionLease(session, "auto", "cpu");
        }
        catch (DllNotFoundException ex)
        {
            notes.Add($"Auto route fell back to CPU because DirectML dependencies were unavailable: {ex.Message}");
            var session = new InferenceSession(modelPath, CreateSessionOptions());
            return new BenchmarkSessionLease(session, "auto", "cpu");
        }
    }

    private static SessionOptions CreateSessionOptions() =>
        new()
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

    private static BenchmarkMeasurements EmptyMeasurements() =>
        new(
            ColdLoadMilliseconds: null,
            WarmupMilliseconds: null,
            WarmLatencyAverageMilliseconds: null,
            WarmLatencyMinimumMilliseconds: null,
            WarmLatencyMaximumMilliseconds: null,
            AudioDurationSeconds: null,
            RealTimeFactorAverage: null);

    private static double? CalculateRealTimeFactorAverage(
        double? audioDurationSeconds,
        double averageLatencyMilliseconds)
    {
        if (audioDurationSeconds is null || audioDurationSeconds <= 0)
        {
            return null;
        }

        return (averageLatencyMilliseconds / 1000d) / audioDurationSeconds.Value;
    }

    private static BenchmarkReport CreateReport(
        string scenario,
        BenchmarkStatus status,
        string modelPath,
        string reportPath,
        string requestedProvider,
        string selectedProvider,
        int runCount,
        bool supportsExecution,
        long modelSizeBytes,
        BenchmarkMeasurements measurements,
        string? failureReason,
        IReadOnlyList<string> notes) =>
        new(
            Scenario: scenario,
            ModelPath: modelPath,
            ReportPath: reportPath,
            Status: status,
            RequestedProvider: requestedProvider,
            SelectedProvider: selectedProvider,
            RunCount: runCount,
            SupportsExecution: supportsExecution,
            ModelSizeBytes: modelSizeBytes,
            Measurements: measurements,
            FailureReason: failureReason,
            Notes: notes,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

    private static BenchmarkInputSet CreateInputs(
        string modelPath,
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata)
    {
        var values = new List<NamedOnnxValue>(inputMetadata.Count);
        var modelProfile = DetermineModelProfile(modelPath, inputMetadata);
        double? audioDurationSeconds = null;

        foreach (var pair in inputMetadata)
        {
            values.Add(CreateInputValue(modelProfile, pair.Key, pair.Value));
        }

        if (modelProfile is BenchmarkModelProfile.SileroVad)
        {
            audioDurationSeconds = 512d / 16000d;
        }
        else if (modelProfile is BenchmarkModelProfile.WhisperEncoder)
        {
            audioDurationSeconds = ResolveWhisperAudioDurationSeconds(inputMetadata);
        }

        return new BenchmarkInputSet(values, audioDurationSeconds);
    }

    private static NamedOnnxValue CreateInputValue(
        BenchmarkModelProfile modelProfile,
        string inputName,
        NodeMetadata metadata)
    {
        if (!metadata.IsTensor)
        {
            throw new NotSupportedException($"Input '{inputName}' is not a tensor input.");
        }

        return metadata.ElementDataType switch
        {
            TensorElementType.Float => NamedOnnxValue.CreateFromTensor(inputName, CreateFloatTensor(modelProfile, inputName, metadata)),
            TensorElementType.Int64 => NamedOnnxValue.CreateFromTensor(inputName, CreateInt64Tensor(modelProfile, inputName, metadata)),
            _ => throw new NotSupportedException($"Input '{inputName}' uses unsupported tensor element type '{metadata.ElementDataType}'.")
        };
    }

    private static DenseTensor<float> CreateFloatTensor(
        BenchmarkModelProfile modelProfile,
        string inputName,
        NodeMetadata metadata)
    {
        var dimensions = ResolveDimensions(modelProfile, inputName, metadata);
        var count = CountElements(dimensions);
        var data = new float[count];

        if (modelProfile is BenchmarkModelProfile.SileroVad && inputName.Equals("input", StringComparison.Ordinal))
        {
            if (data.Length > 0)
            {
                data[0] = 0.1f;
            }
        }
        else if (modelProfile is BenchmarkModelProfile.WhisperEncoder && inputName.Equals("input_features", StringComparison.Ordinal))
        {
            for (var index = 0; index < data.Length; index++)
            {
                data[index] = (index % 11) * 0.01f;
            }
        }

        return new DenseTensor<float>(data, dimensions);
    }

    private static DenseTensor<long> CreateInt64Tensor(
        BenchmarkModelProfile modelProfile,
        string inputName,
        NodeMetadata metadata)
    {
        var dimensions = ResolveDimensions(modelProfile, inputName, metadata);
        var count = CountElements(dimensions);
        var data = new long[count];

        if (modelProfile is BenchmarkModelProfile.SileroVad && inputName.Equals("sr", StringComparison.Ordinal) && data.Length > 0)
        {
            data[0] = 16000L;
        }
        else if (modelProfile is BenchmarkModelProfile.OpusEncoder)
        {
            FillOpusEncoderTensor(inputName, data);
        }

        return new DenseTensor<long>(data, dimensions);
    }

    private static int[] ResolveDimensions(BenchmarkModelProfile modelProfile, string inputName, NodeMetadata metadata)
    {
        if (modelProfile is BenchmarkModelProfile.SileroVad)
        {
            if (inputName.Equals("input", StringComparison.Ordinal))
            {
                return [1, 512];
            }

            if (inputName.Equals("state", StringComparison.Ordinal))
            {
                return [2, 1, 128];
            }

            if (inputName.Equals("sr", StringComparison.Ordinal))
            {
                return [1];
            }
        }
        else if (modelProfile is BenchmarkModelProfile.WhisperEncoder && inputName.Equals("input_features", StringComparison.Ordinal))
        {
            return [1, 80, 3000];
        }
        else if (modelProfile is BenchmarkModelProfile.OpusEncoder)
        {
            return inputName switch
            {
                "input_ids" => [1, 8],
                "attention_mask" => [1, 8],
                _ => metadata.Dimensions.Select(ToPositiveDimension).ToArray()
            };
        }

        return metadata.Dimensions.Select(ToPositiveDimension).ToArray();
    }

    private static BenchmarkModelProfile DetermineModelProfile(
        string modelPath,
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata)
    {
        if (LooksLikeSileroVad(modelPath, inputMetadata))
        {
            return BenchmarkModelProfile.SileroVad;
        }

        if (LooksLikeWhisperEncoder(modelPath))
        {
            return BenchmarkModelProfile.WhisperEncoder;
        }

        if (LooksLikeOpusEncoder(modelPath))
        {
            return BenchmarkModelProfile.OpusEncoder;
        }

        return BenchmarkModelProfile.Generic;
    }

    private static bool LooksLikeSileroVad(
        string modelPath,
        IReadOnlyDictionary<string, NodeMetadata>? inputMetadata)
    {
        var normalizedPath = modelPath.Replace('/', '\\');
        if (normalizedPath.Contains("silero", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (inputMetadata is null)
        {
            return false;
        }

        return inputMetadata.ContainsKey("input")
            && inputMetadata.ContainsKey("state")
            && inputMetadata.ContainsKey("sr");
    }

    private static string ResolveModelPath(string suppliedPath, ICollection<string> notes)
    {
        BenchmarkModelCandidate resolution = BenchmarkModelPathResolver.CreateDefault().ResolveSingle(suppliedPath);
        notes.Add(resolution.ResolutionNote);
        return resolution.ModelPath;
    }

    private static bool LooksLikeWhisperEncoder(string modelPath)
    {
        var fileName = Path.GetFileName(modelPath);
        var parentDirectory = Path.GetDirectoryName(modelPath) ?? string.Empty;
        return fileName.StartsWith("encoder_model", StringComparison.OrdinalIgnoreCase)
            && parentDirectory.Contains("whisper", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOpusEncoder(string modelPath)
    {
        var fileName = Path.GetFileName(modelPath);
        var parentDirectory = Path.GetDirectoryName(modelPath) ?? string.Empty;
        return fileName.StartsWith("encoder_model", StringComparison.OrdinalIgnoreCase)
            && parentDirectory.Contains("opus", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWhisperDecoderPath(string encoderModelPath)
    {
        var decoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model.onnx");
        if (!File.Exists(decoderModelPath))
        {
            throw new FileNotFoundException("Whisper decoder model was not found next to the encoder model.", decoderModelPath);
        }

        return Path.GetFullPath(decoderModelPath);
    }

    private static int ResolveWhisperDecoderStartTokenId(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return 50258;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (document.RootElement.TryGetProperty("decoder_start_token_id", out var tokenIdElement) &&
            tokenIdElement.TryGetInt32(out var tokenId))
        {
            return tokenId;
        }

        return 50258;
    }

    private static string ResolveOpusDecoderPath(string encoderModelPath)
    {
        var decoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model.onnx");
        if (File.Exists(decoderModelPath))
        {
            return Path.GetFullPath(decoderModelPath);
        }

        var mergedDecoderModelPath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model_merged.onnx");
        if (!File.Exists(mergedDecoderModelPath))
        {
            throw new FileNotFoundException("Opus decoder model was not found next to the encoder model.", decoderModelPath);
        }

        return Path.GetFullPath(mergedDecoderModelPath);
    }

    private static int ResolveOpusDecoderStartTokenId(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return 65000;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (document.RootElement.TryGetProperty("decoder_start_token_id", out var tokenIdElement) &&
            tokenIdElement.TryGetInt32(out var tokenId))
        {
            return tokenId;
        }

        return 65000;
    }

    private static double? ResolveWhisperAudioDurationSeconds(
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata)
    {
        if (!inputMetadata.TryGetValue("input_features", out var metadata))
        {
            return null;
        }

        var dimensions = metadata.Dimensions.Select(ToPositiveDimension).ToArray();
        if (dimensions.Length < 3)
        {
            return null;
        }

        return dimensions[2] / 100d;
    }

    private static void RunWhisperPass(
        WhisperSessionLease lease,
        BenchmarkInputSet encoderInputSet,
        int decoderStartTokenId)
    {
        using var encoderResults = lease.EncoderSession.Run(encoderInputSet.Values);
        var encoderHiddenStates = encoderResults.First().AsTensor<float>();
        using var decoderInputs = CreateWhisperDecoderInputs(
            lease.DecoderSession.InputMetadata,
            encoderHiddenStates,
            decoderStartTokenId);
        using var decoderResults = lease.DecoderSession.Run(decoderInputs.Values);
    }

    private static void RunOpusPass(
        OpusSessionLease lease,
        BenchmarkInputSet encoderInputSet,
        int decoderStartTokenId)
    {
        using var encoderResults = lease.EncoderSession.Run(encoderInputSet.Values);
        var encoderHiddenStates = encoderResults.First().AsTensor<float>();
        using var decoderInputs = CreateOpusDecoderInputs(
            lease.DecoderSession.InputMetadata,
            encoderHiddenStates,
            decoderStartTokenId,
            encoderInputSet.Values);
        using var decoderResults = lease.DecoderSession.Run(decoderInputs.Values);
    }

    private static DecoderInputSet CreateWhisperDecoderInputs(
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata,
        Tensor<float> encoderHiddenStates,
        int decoderStartTokenId)
    {
        var values = new List<NamedOnnxValue>(inputMetadata.Count);

        foreach (var pair in inputMetadata)
        {
            values.Add(pair.Key switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor(
                    "input_ids",
                    new DenseTensor<long>(new long[] { decoderStartTokenId }, [1, 1])),
                "encoder_hidden_states" => NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                _ => throw new NotSupportedException($"Whisper decoder input '{pair.Key}' is not supported by the benchmark harness yet.")
            });
        }

        return new DecoderInputSet(values);
    }

    private static DecoderInputSet CreateOpusDecoderInputs(
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata,
        Tensor<float> encoderHiddenStates,
        int decoderStartTokenId,
        IReadOnlyList<NamedOnnxValue> encoderInputs)
    {
        var values = new List<NamedOnnxValue>(inputMetadata.Count);
        var encoderAttentionMask = encoderInputs
            .FirstOrDefault(static value => value.Name.Equals("attention_mask", StringComparison.Ordinal))
            ?.AsTensor<long>();

        foreach (var pair in inputMetadata)
        {
            values.Add(pair.Key switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor(
                    "input_ids",
                    new DenseTensor<long>(new long[] { decoderStartTokenId }, [1, 1])),
                "encoder_hidden_states" => NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                "attention_mask" when encoderAttentionMask is not null => NamedOnnxValue.CreateFromTensor("attention_mask", encoderAttentionMask),
                "encoder_attention_mask" when encoderAttentionMask is not null => NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderAttentionMask),
                _ => throw new NotSupportedException($"Opus decoder input '{pair.Key}' is not supported by the benchmark harness yet.")
            });
        }

        return new DecoderInputSet(values);
    }

    private static WhisperSessionLease CreateWhisperSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        BenchmarkProviderPreference preference,
        ICollection<string> notes) =>
        preference switch
        {
            BenchmarkProviderPreference.Cpu => CreateCpuWhisperSessionLease(encoderModelPath, decoderModelPath, notes),
            BenchmarkProviderPreference.Dml => CreateDirectMlWhisperSessionLease(encoderModelPath, decoderModelPath, notes),
            BenchmarkProviderPreference.Auto => CreateAutoWhisperSessionLease(encoderModelPath, decoderModelPath, notes),
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown provider preference.")
        };

    private static OpusSessionLease CreateOpusSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        BenchmarkProviderPreference preference,
        ICollection<string> notes) =>
        preference switch
        {
            BenchmarkProviderPreference.Cpu => CreateCpuOpusSessionLease(encoderModelPath, decoderModelPath, notes),
            BenchmarkProviderPreference.Dml => CreateDirectMlOpusSessionLease(encoderModelPath, decoderModelPath, notes),
            BenchmarkProviderPreference.Auto => CreateAutoOpusSessionLease(encoderModelPath, decoderModelPath, notes),
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown provider preference.")
        };

    private static WhisperSessionLease CreateCpuWhisperSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        ICollection<string> notes)
    {
        notes.Add("Provider route: explicit CPU execution provider.");
        return new WhisperSessionLease(
            new InferenceSession(encoderModelPath, CreateSessionOptions()),
            new InferenceSession(decoderModelPath, CreateSessionOptions()),
            "cpu",
            "cpu");
    }

    private static WhisperSessionLease CreateDirectMlWhisperSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        ICollection<string> notes)
    {
        notes.Add("Provider route: explicit DirectML execution provider.");
        return new WhisperSessionLease(
            CreateDirectMlSession(encoderModelPath),
            CreateDirectMlSession(decoderModelPath),
            "dml",
            "dml");
    }

    private static WhisperSessionLease CreateAutoWhisperSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        ICollection<string> notes)
    {
        notes.Add("Provider route: auto selected.");

        try
        {
            var encoderSession = CreateDirectMlSession(encoderModelPath);
            var decoderSession = CreateDirectMlSession(decoderModelPath);
            notes.Add("Auto route resolved to DirectML.");
            return new WhisperSessionLease(encoderSession, decoderSession, "auto", "dml");
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
        {
            notes.Add($"Auto route fell back to CPU because DirectML was unavailable: {ex.Message}");
            return new WhisperSessionLease(
                new InferenceSession(encoderModelPath, CreateSessionOptions()),
                new InferenceSession(decoderModelPath, CreateSessionOptions()),
                "auto",
                "cpu");
        }
    }

    private static OpusSessionLease CreateCpuOpusSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        ICollection<string> notes)
    {
        notes.Add("Provider route: explicit CPU execution provider.");
        return new OpusSessionLease(
            new InferenceSession(encoderModelPath, CreateSessionOptions()),
            new InferenceSession(decoderModelPath, CreateSessionOptions()),
            "cpu",
            "cpu");
    }

    private static OpusSessionLease CreateDirectMlOpusSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        ICollection<string> notes)
    {
        notes.Add("Provider route: explicit DirectML execution provider.");
        return new OpusSessionLease(
            CreateDirectMlSession(encoderModelPath),
            CreateDirectMlSession(decoderModelPath),
            "dml",
            "dml");
    }

    private static OpusSessionLease CreateAutoOpusSessionLease(
        string encoderModelPath,
        string decoderModelPath,
        ICollection<string> notes)
    {
        notes.Add("Provider route: auto selected.");

        try
        {
            var encoderSession = CreateDirectMlSession(encoderModelPath);
            var decoderSession = CreateDirectMlSession(decoderModelPath);
            notes.Add("Auto route resolved to DirectML.");
            return new OpusSessionLease(encoderSession, decoderSession, "auto", "dml");
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
        {
            notes.Add($"Auto route fell back to CPU because DirectML was unavailable: {ex.Message}");
            return new OpusSessionLease(
                new InferenceSession(encoderModelPath, CreateSessionOptions()),
                new InferenceSession(decoderModelPath, CreateSessionOptions()),
                "auto",
                "cpu");
        }
    }

    private static InferenceSession CreateDirectMlSession(string modelPath)
    {
        var options = CreateSessionOptions();
        options.AppendExecutionProvider_DML();
        return new InferenceSession(modelPath, options);
    }

    private static void FillOpusEncoderTensor(string inputName, long[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        if (inputName.Equals("attention_mask", StringComparison.Ordinal))
        {
            Array.Fill(data, 1L);
            return;
        }

        if (inputName.Equals("input_ids", StringComparison.Ordinal))
        {
            long[] sampleTokens = [250, 142, 77, 901, 54, 12, 0, 65000];
            for (var index = 0; index < data.Length; index++)
            {
                data[index] = sampleTokens[index % sampleTokens.Length];
            }
        }
    }

    private static string ResolveFailureScenario(string modelPath)
    {
        if (LooksLikeWhisperEncoder(modelPath))
        {
            return "whisper-encoder-decoder";
        }

        if (LooksLikeOpusEncoder(modelPath))
        {
            return "opus-mt-encoder-decoder";
        }

        return "onnx-model";
    }

    private static int ToPositiveDimension(int dimension) => dimension <= 0 ? 1 : dimension;

    private static int CountElements(IEnumerable<int> dimensions)
    {
        var count = 1;
        foreach (var dimension in dimensions)
        {
            checked
            {
                count *= dimension;
            }
        }

        return count;
    }

    private sealed record BenchmarkInputSet(
        IReadOnlyList<NamedOnnxValue> Values,
        double? AudioDurationSeconds);

    private sealed class DecoderInputSet : IDisposable
    {
        public DecoderInputSet(IReadOnlyList<NamedOnnxValue> values)
        {
            Values = values;
        }

        public IReadOnlyList<NamedOnnxValue> Values { get; }

        public void Dispose()
        {
            foreach (var value in Values.OfType<IDisposable>())
            {
                value.Dispose();
            }
        }
    }

    private sealed record BenchmarkSessionLease(
        InferenceSession Session,
        string RequestedProvider,
        string SelectedProvider) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    private sealed record WhisperSessionLease(
        InferenceSession EncoderSession,
        InferenceSession DecoderSession,
        string RequestedProvider,
        string SelectedProvider) : IDisposable
    {
        public void Dispose()
        {
            DecoderSession.Dispose();
            EncoderSession.Dispose();
        }
    }

    private sealed record OpusSessionLease(
        InferenceSession EncoderSession,
        InferenceSession DecoderSession,
        string RequestedProvider,
        string SelectedProvider) : IDisposable
    {
        public void Dispose()
        {
            DecoderSession.Dispose();
            EncoderSession.Dispose();
        }
    }

    private sealed record BenchmarkExecution(
        string Scenario,
        string RequestedProvider,
        string SelectedProvider,
        long ModelSizeBytes,
        double ColdLoadMilliseconds,
        double WarmupMilliseconds,
        double WarmLatencyAverageMilliseconds,
        double WarmLatencyMinimumMilliseconds,
        double WarmLatencyMaximumMilliseconds,
        double? AudioDurationSeconds);

    private enum BenchmarkModelProfile
    {
        Generic,
        SileroVad,
        WhisperEncoder,
        OpusEncoder
    }
}
