namespace BabelStudio.Inference.Onnx.WindowsMl;

public sealed class WindowsMlExecutionProviderBootstrapper
{
    private const string WindowsTargetingRequiredReason =
        "Microsoft Learn's current C# Windows ML guidance requires .NET 8+ with a Windows-specific target framework such as net8.0-windows10.0.19041.0 or greater. " +
        "This build is using the base net10.0 target, so Windows ML bootstrap remains deferred until a Windows-targeted consumer selects the net10.0-windows10.0.19041.0 asset.";

    public Task<WindowsMlBootstrapResult> RegisterInstalledCertifiedAsync(CancellationToken cancellationToken) =>
        CreateDeferredResultAsync(WindowsMlBootstrapMode.RegisterInstalledCertified, cancellationToken);

    public Task<WindowsMlBootstrapResult> EnsureAndRegisterCertifiedAsync(CancellationToken cancellationToken) =>
        CreateDeferredResultAsync(WindowsMlBootstrapMode.EnsureAndRegisterCertified, cancellationToken);

    private static Task<WindowsMlBootstrapResult> CreateDeferredResultAsync(
        WindowsMlBootstrapMode mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WindowsMlBootstrapResult(mode, false, WindowsTargetingRequiredReason));
    }
}
