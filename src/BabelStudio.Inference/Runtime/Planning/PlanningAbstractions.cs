using BabelStudio.Domain;

namespace BabelStudio.Inference.Runtime.Planning;

public interface IRuntimePlanner
{
    Task<StageRuntimePlan> PlanAsync(
        StageRuntimePlanningRequest request,
        CancellationToken cancellationToken = default);
}

public interface IHardwareProfileProvider
{
    Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default);
}

public interface IExecutionProviderDiscovery
{
    Task<IReadOnlyList<ExecutionProviderAvailability>> DiscoverAsync(
        HardwareProfile hardwareProfile,
        CancellationToken cancellationToken = default);
}

public interface IExecutionProviderSmokeTester
{
    Task<ExecutionProviderSmokeTestResult> SmokeTestAsync(
        ExecutionProviderSmokeTestRequest request,
        CancellationToken cancellationToken = default);
}

public interface IModelCacheInventory
{
    Task<IReadOnlyList<LocalModelCacheRecord>> LoadAsync(CancellationToken cancellationToken = default);
}
