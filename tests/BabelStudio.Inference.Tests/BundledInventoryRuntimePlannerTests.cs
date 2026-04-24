using BabelStudio.Composition.Runtime.Planning;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx.Runtime.Planning;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;
using BabelStudio.TestDoubles;

namespace BabelStudio.Inference.Tests;

public sealed class BundledInventoryRuntimePlannerTests
{
    [RequiresBundledModelFact("silero-vad")]
    public async Task RuntimePlanner_UsesBundledManifestInventoryForVadCpuPlan()
    {
        Assert.True(BundledModelManifestRegistry.TryLoadDefault(out BundledModelManifestRegistry? registry, out string? error), error);
        Assert.NotNull(registry);

        var planner = new RuntimePlanner(
            registry!,
            new CommercialSafeEvaluator(),
            new MachineHardwareProfileProvider(),
            new OnnxExecutionProviderDiscovery(),
            new PassingSmokeTester(),
            new BundledManifestModelCacheInventory(registry));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: false));

        Assert.Equal(StageRuntimePlanStatus.Ready, plan.Status);
        Assert.Equal("onnx-community/silero-vad", plan.ModelId);
        Assert.NotNull(plan.ExecutionProvider);
    }

    private sealed class PassingSmokeTester : IExecutionProviderSmokeTester
    {
        public Task<ExecutionProviderSmokeTestResult> SmokeTestAsync(
            ExecutionProviderSmokeTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionProviderSmokeTestResult(true));
    }
}
