using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Inference.Onnx.Runtime.Planning;

public sealed class OnnxExecutionProviderDiscovery : IExecutionProviderDiscovery
{
    public Task<IReadOnlyList<ExecutionProviderAvailability>> DiscoverAsync(
        HardwareProfile hardwareProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hardwareProfile);
        cancellationToken.ThrowIfCancellationRequested();

        var availabilities = new List<ExecutionProviderAvailability>
        {
            new(ExecutionProviderKind.Cpu, true, "CPU execution is always available.")
        };

        bool directMlAvailable = hardwareProfile.OperatingSystem.Equals("windows", StringComparison.OrdinalIgnoreCase) &&
                                 hardwareProfile.HasGpu;
        availabilities.Add(directMlAvailable
            ? new ExecutionProviderAvailability(
                ExecutionProviderKind.DirectMl,
                true,
                "Windows runtime planning can probe DirectML on this machine.")
            : new ExecutionProviderAvailability(
                ExecutionProviderKind.DirectMl,
                false,
                "DirectML probing is only enabled on Windows hosts with a GPU-capable runtime path."));

        return Task.FromResult<IReadOnlyList<ExecutionProviderAvailability>>(availabilities);
    }
}
