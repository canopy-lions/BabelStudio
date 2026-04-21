using System.Runtime.Versioning;
using Microsoft.Windows.AI.MachineLearning;

namespace BabelStudio.Inference.Onnx.WindowsMl;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsMlExecutionProviderBootstrapper
{
    public async Task<WindowsMlBootstrapResult> RegisterInstalledCertifiedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var catalog = ExecutionProviderCatalog.GetDefault();
            await catalog.RegisterCertifiedAsync();
            return new WindowsMlBootstrapResult(WindowsMlBootstrapMode.RegisterInstalledCertified, true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WindowsMlBootstrapResult(WindowsMlBootstrapMode.RegisterInstalledCertified, false, ex.Message);
        }
    }

    public async Task<WindowsMlBootstrapResult> EnsureAndRegisterCertifiedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var catalog = ExecutionProviderCatalog.GetDefault();
            await catalog.EnsureAndRegisterCertifiedAsync();
            return new WindowsMlBootstrapResult(WindowsMlBootstrapMode.EnsureAndRegisterCertified, true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WindowsMlBootstrapResult(WindowsMlBootstrapMode.EnsureAndRegisterCertified, false, ex.Message);
        }
    }
}
