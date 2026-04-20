using BabelStudio.Domain;

namespace BabelStudio.Inference.Runtime.Planning;

public sealed record StageRuntimePlan
{
    public RuntimeStage Stage { get; init; }

    public StageRuntimePlanStatus Status { get; init; }

    public string? ModelId { get; init; }

    public string? ModelAlias { get; init; }

    public string? Variant { get; init; }

    public ExecutionProviderKind? ExecutionProvider { get; init; }

    public RuntimePlanFallback? Fallback { get; init; }

    public IReadOnlyList<RuntimePlanWarning> Warnings { get; init; } = [];
}

public sealed record RuntimePlanFallback(
    RuntimePlanFallbackCode Code,
    string? Detail = null);

public sealed record RuntimePlanWarning(
    RuntimePlanWarningCode Code,
    string? Detail = null);

public sealed record HardwareProfile(
    string OperatingSystem,
    string Architecture,
    bool HasGpu,
    string? GpuDescription = null);

public sealed record ExecutionProviderAvailability(
    ExecutionProviderKind Provider,
    bool IsAvailable,
    string? Detail = null);

public sealed record ExecutionProviderSmokeTestRequest(
    RuntimeStage Stage,
    string ModelId,
    string ModelAlias,
    string Variant,
    ExecutionProviderKind ExecutionProvider,
    string ModelRootPath,
    string EntryPath);

public sealed record ExecutionProviderSmokeTestResult(
    bool Passed,
    string? Detail = null);
