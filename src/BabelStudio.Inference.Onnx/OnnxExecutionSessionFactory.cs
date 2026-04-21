using BabelStudio.Domain;
using BabelStudio.Inference.Onnx.WindowsMl;
using Microsoft.ML.OnnxRuntime;

namespace BabelStudio.Inference.Onnx;

internal static class OnnxExecutionSessionFactory
{
    public static async Task<SingleSessionLease> CreateSingleAsync(
        string modelPath,
        ExecutionProviderKind provider,
        CancellationToken cancellationToken)
    {
        string requestedProvider = provider is ExecutionProviderKind.Cpu ? "cpu" : "auto";
        string selectedProvider = provider is ExecutionProviderKind.Cpu ? "cpu" : "dml";
        WindowsMlBootstrapResult? bootstrapResult = await BootstrapIfNeededAsync(provider, cancellationToken).ConfigureAwait(false);
        SessionOptions options = CreateSessionOptions(provider);
        string? bootstrapDetail = FormatBootstrapDetail(provider, bootstrapResult);
        return new SingleSessionLease(new InferenceSession(modelPath, options), requestedProvider, selectedProvider, bootstrapDetail);
    }

    public static async Task<WhisperSessionLease> CreateWhisperAsync(
        string encoderModelPath,
        string decoderModelPath,
        ExecutionProviderKind provider,
        CancellationToken cancellationToken)
    {
        string requestedProvider = provider is ExecutionProviderKind.Cpu ? "cpu" : "auto";
        string selectedProvider = provider is ExecutionProviderKind.Cpu ? "cpu" : "dml";
        WindowsMlBootstrapResult? bootstrapResult = await BootstrapIfNeededAsync(provider, cancellationToken).ConfigureAwait(false);
        SessionOptions encoderOptions = CreateSessionOptions(provider);
        SessionOptions decoderOptions = CreateSessionOptions(provider);
        string? bootstrapDetail = FormatBootstrapDetail(provider, bootstrapResult);

        return new WhisperSessionLease(
            new InferenceSession(encoderModelPath, encoderOptions),
            new InferenceSession(decoderModelPath, decoderOptions),
            requestedProvider,
            selectedProvider,
            bootstrapDetail);
    }

    private static SessionOptions CreateSessionOptions(ExecutionProviderKind provider)
    {
        SessionOptions options = CreateSessionOptions();
        if (provider is ExecutionProviderKind.DirectMl)
        {
            options.AppendExecutionProvider_DML();
        }

        return options;
    }

    private static SessionOptions CreateSessionOptions() =>
        new()
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

    private static string? FormatBootstrapDetail(
        ExecutionProviderKind provider,
        WindowsMlBootstrapResult? bootstrapResult)
    {
        if (provider is ExecutionProviderKind.Cpu)
        {
            return "Windows ML bootstrap skipped for CPU-only provider route.";
        }

        WindowsMlBootstrapResult result = bootstrapResult ?? new WindowsMlBootstrapResult(
            WindowsMlBootstrapMode.EnsureAndRegisterCertified,
            Succeeded: false,
            FailureReason: null);
        if (result.Succeeded)
        {
            return $"Windows ML bootstrap succeeded via {result.Mode}.";
        }

        return string.IsNullOrWhiteSpace(result.FailureReason)
            ? "Windows ML bootstrap did not complete."
            : $"Windows ML bootstrap did not complete: {result.FailureReason}";
    }

    private static Task<WindowsMlBootstrapResult?> BootstrapIfNeededAsync(
        ExecutionProviderKind provider,
        CancellationToken cancellationToken)
    {
        if (provider is ExecutionProviderKind.Cpu)
        {
            return Task.FromResult<WindowsMlBootstrapResult?>(null);
        }

        return EnsureWindowsMlBootstrapAsync(cancellationToken);
    }

    private static async Task<WindowsMlBootstrapResult?> EnsureWindowsMlBootstrapAsync(CancellationToken cancellationToken)
    {
        var bootstrapper = new WindowsMlExecutionProviderBootstrapper();
        return await bootstrapper.EnsureAndRegisterCertifiedAsync(cancellationToken).ConfigureAwait(false);
    }

    internal sealed record SingleSessionLease(
        InferenceSession Session,
        string RequestedProvider,
        string SelectedProvider,
        string? BootstrapDetail) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    internal sealed record WhisperSessionLease(
        InferenceSession EncoderSession,
        InferenceSession DecoderSession,
        string RequestedProvider,
        string SelectedProvider,
        string? BootstrapDetail) : IDisposable
    {
        public void Dispose()
        {
            DecoderSession.Dispose();
            EncoderSession.Dispose();
        }
    }
}
