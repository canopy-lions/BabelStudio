using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.Planning;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.Runtime.Planning;

public sealed class OnnxExecutionProviderSmokeTester : IExecutionProviderSmokeTester
{
    public async Task<ExecutionProviderSmokeTestResult> SmokeTestAsync(
        ExecutionProviderSmokeTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            switch (request.Stage)
            {
                case RuntimeStage.Vad:
                    await SmokeTestVadAsync(request.EntryPath, request.ExecutionProvider, cancellationToken).ConfigureAwait(false);
                    break;
                case RuntimeStage.Asr:
                    await SmokeTestWhisperAsync(request.EntryPath, request.ExecutionProvider, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return new ExecutionProviderSmokeTestResult(
                        false,
                        $"Smoke testing is not implemented for runtime stage '{request.Stage}'.");
            }

            return new ExecutionProviderSmokeTestResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ExecutionProviderSmokeTestResult(false, ex.Message);
        }
    }

    private static async Task SmokeTestVadAsync(
        string modelPath,
        ExecutionProviderKind provider,
        CancellationToken cancellationToken)
    {
        using OnnxExecutionSessionFactory.SingleSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateSingleAsync(modelPath, provider, cancellationToken)
            .ConfigureAwait(false);

        using var input = CreateVadInputs();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> _ = sessionLease.Session.Run(input.Values);
    }

    private static async Task SmokeTestWhisperAsync(
        string encoderModelPath,
        ExecutionProviderKind provider,
        CancellationToken cancellationToken)
    {
        string decoderModelPath = ResolveWhisperDecoderPath(encoderModelPath);
        using OnnxExecutionSessionFactory.WhisperSessionLease sessionLease = await OnnxExecutionSessionFactory
            .CreateWhisperAsync(encoderModelPath, decoderModelPath, provider, cancellationToken)
            .ConfigureAwait(false);

        using var encoderInputs = CreateWhisperEncoderInputs();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encoderResults = sessionLease.EncoderSession.Run(encoderInputs.Values);
        Tensor<float> hiddenStates = encoderResults.Single().AsTensor<float>();

        using var decoderInputs = CreateWhisperDecoderInputs(hiddenStates);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> _ = sessionLease.DecoderSession.Run(decoderInputs.Values);
    }

    private static InputSet CreateVadInputs()
    {
        IReadOnlyList<NamedOnnxValue> values =
        [
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(new float[512], [1, 512])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(new float[2 * 128], [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { 16000 }, [1]))
        ];

        return new InputSet(values);
    }

    private static InputSet CreateWhisperEncoderInputs()
    {
        IReadOnlyList<NamedOnnxValue> values =
        [
            NamedOnnxValue.CreateFromTensor("input_features", new DenseTensor<float>(new float[80 * 3000], [1, 80, 3000]))
        ];

        return new InputSet(values);
    }

    private static InputSet CreateWhisperDecoderInputs(Tensor<float> encoderHiddenStates)
    {
        IReadOnlyList<NamedOnnxValue> values =
        [
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new long[] { 50258 }, [1, 1])),
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates)
        ];

        return new InputSet(values);
    }

    private static string ResolveWhisperDecoderPath(string encoderModelPath)
    {
        string fileName = Path.GetFileName(encoderModelPath);
        string decoderFileName = fileName.Replace("encoder_model", "decoder_model", StringComparison.OrdinalIgnoreCase);
        string candidatePath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, decoderFileName);
        if (File.Exists(candidatePath))
        {
            return Path.GetFullPath(candidatePath);
        }

        candidatePath = Path.Combine(Path.GetDirectoryName(encoderModelPath)!, "decoder_model.onnx");
        if (File.Exists(candidatePath))
        {
            return Path.GetFullPath(candidatePath);
        }

        throw new FileNotFoundException("Whisper decoder model was not found next to the encoder model.", candidatePath);
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
}
