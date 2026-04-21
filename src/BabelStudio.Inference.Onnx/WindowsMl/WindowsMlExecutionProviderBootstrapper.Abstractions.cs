namespace BabelStudio.Inference.Onnx.WindowsMl;

public enum WindowsMlBootstrapMode
{
    RegisterInstalledCertified,
    EnsureAndRegisterCertified
}

public sealed record WindowsMlBootstrapResult(
    WindowsMlBootstrapMode Mode,
    bool Succeeded,
    string? FailureReason);
