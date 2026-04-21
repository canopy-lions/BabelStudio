using System.Runtime.InteropServices;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Inference.Onnx.Runtime.Planning;

public sealed class MachineHardwareProfileProvider : IHardwareProfileProvider
{
    public Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string operatingSystem = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : "unknown";

        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        bool hasGpu = OperatingSystem.IsWindows();

        return Task.FromResult(new HardwareProfile(
            operatingSystem,
            architecture,
            hasGpu,
            hasGpu ? "Windows GPU route available for DirectML probing." : null));
    }
}
