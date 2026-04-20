using System.Text.Json;
using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;
using BabelStudio.Infrastructure.Persistence.Repositories;
using BabelStudio.Infrastructure.Runtime.Planning;
using BabelStudio.Infrastructure.Settings;

namespace BabelStudio.Inference.Tests;

public sealed class RuntimePlannerTests
{
    [Fact]
    public async Task PlanAsync_WhenDirectMlSmokePasses_ReturnsReadyDirectMl()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateVadSpec("silero-vad", commercialAllowed: true, license: "MIT"));

        string cacheRoot = workspace.CreateCacheRoot("onnx-community/silero-vad");
        workspace.WriteCacheFile(cacheRoot, "onnx/model_fp16.onnx");

        RuntimePlanner planner = CreatePlanner(
            registry,
            [ new("onnx-community/silero-vad", cacheRoot, "main", "sha", DateTimeOffset.UtcNow) ],
            [ new(ExecutionProviderKind.DirectMl, true) ],
            request => new ExecutionProviderSmokeTestResult(
                request.ExecutionProvider is ExecutionProviderKind.DirectMl &&
                request.Variant.Equals("fp16", StringComparison.OrdinalIgnoreCase)));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: false));

        Assert.Equal(StageRuntimePlanStatus.Ready, plan.Status);
        Assert.Equal(ExecutionProviderKind.DirectMl, plan.ExecutionProvider);
        Assert.Equal("silero-vad", plan.ModelAlias);
        Assert.Equal("fp16", plan.Variant);
        Assert.Null(plan.Fallback);
    }

    [Fact]
    public async Task PlanAsync_WhenDirectMlSmokeFails_FallsBackToCpu()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateVadSpec("silero-vad", commercialAllowed: true, license: "MIT"));

        string cacheRoot = workspace.CreateCacheRoot("onnx-community/silero-vad");
        workspace.WriteCacheFile(cacheRoot, "onnx/model_fp16.onnx");
        workspace.WriteCacheFile(cacheRoot, "onnx/model_int8.onnx");

        var smokeRequests = new List<ExecutionProviderSmokeTestRequest>();
        RuntimePlanner planner = CreatePlanner(
            registry,
            [ new("onnx-community/silero-vad", cacheRoot, "main", "sha", DateTimeOffset.UtcNow) ],
            [ new(ExecutionProviderKind.DirectMl, true) ],
            request =>
            {
                smokeRequests.Add(request);
                return new ExecutionProviderSmokeTestResult(false, "DirectML smoke-test failed.");
            });

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: false));

        Assert.Equal(StageRuntimePlanStatus.Ready, plan.Status);
        Assert.Equal(ExecutionProviderKind.Cpu, plan.ExecutionProvider);
        Assert.Equal("int8", plan.Variant);
        Assert.NotNull(plan.Fallback);
        Assert.Equal(RuntimePlanFallbackCode.ProviderSmokeTestFailed, plan.Fallback!.Code);
        Assert.Contains(plan.Warnings, warning => warning.Code == RuntimePlanWarningCode.CpuFallback);
        Assert.NotEmpty(smokeRequests);
    }

    [Fact]
    public async Task PlanAsync_WhenModelIsNotCached_ReturnsDownloadRequired()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateVadSpec("silero-vad", commercialAllowed: true, license: "MIT"));

        RuntimePlanner planner = CreatePlanner(
            registry,
            [],
            [ new(ExecutionProviderKind.DirectMl, false, "No DirectML adapter found.") ],
            _ => throw new InvalidOperationException("Smoke tests should not run for download-required plans."));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: false));

        Assert.Equal(StageRuntimePlanStatus.DownloadRequired, plan.Status);
        Assert.Equal(ExecutionProviderKind.Cpu, plan.ExecutionProvider);
        Assert.Equal("int8", plan.Variant);
        Assert.NotNull(plan.Fallback);
        Assert.Equal(RuntimePlanFallbackCode.ModelNotCached, plan.Fallback!.Code);
    }

    [Fact]
    public async Task PlanAsync_CommercialSafeFilteringExcludesUnsafePreferredAlias()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            new ManifestSpec(
                ModelId: "unsafe/vad",
                Task: "vad",
                License: "unknown",
                CommercialAllowed: false,
                RequiresAttribution: false,
                RequiresUserConsent: false,
                VoiceCloning: false,
                Aliases: [ "silero-vad" ],
                RootFolder: "unsafe-vad",
                BenchmarkEntry: "onnx/model.onnx",
                Variants:
                [
                    new ManifestVariantSpec("fp16", "onnx/model_fp16.onnx"),
                    new ManifestVariantSpec("int8", "onnx/model_int8.onnx")
                ]),
            new ManifestSpec(
                ModelId: "safe/vad",
                Task: "vad",
                License: "MIT",
                CommercialAllowed: true,
                RequiresAttribution: false,
                RequiresUserConsent: false,
                VoiceCloning: false,
                Aliases: [ "silero" ],
                RootFolder: "safe-vad",
                BenchmarkEntry: "onnx/model.onnx",
                Variants:
                [
                    new ManifestVariantSpec("fp16", "onnx/model_fp16.onnx"),
                    new ManifestVariantSpec("int8", "onnx/model_int8.onnx")
                ]));

        string safeCacheRoot = workspace.CreateCacheRoot("safe/vad");
        workspace.WriteCacheFile(safeCacheRoot, "onnx/model_int8.onnx");

        RuntimePlanner planner = CreatePlanner(
            registry,
            [ new("safe/vad", safeCacheRoot, "main", "sha", DateTimeOffset.UtcNow) ],
            [ new(ExecutionProviderKind.DirectMl, false, "DirectML disabled for this test.") ],
            _ => throw new InvalidOperationException("Smoke tests should not run for CPU-only plans."));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: true));

        Assert.Equal(StageRuntimePlanStatus.Ready, plan.Status);
        Assert.Equal("safe/vad", plan.ModelId);
        Assert.Equal("silero", plan.ModelAlias);
        Assert.Equal(ExecutionProviderKind.Cpu, plan.ExecutionProvider);
        Assert.Contains(plan.Warnings, warning => warning.Code == RuntimePlanWarningCode.CommercialSafeModeActive);
    }

    [Fact]
    public async Task PlanAsync_WhenAllAsrCandidatesAreCommercialUnsafe_ReturnsBlocked()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            new ManifestSpec(
                ModelId: "unsafe/asr",
                Task: "asr",
                License: "unknown",
                CommercialAllowed: false,
                RequiresAttribution: false,
                RequiresUserConsent: false,
                VoiceCloning: false,
                Aliases: [ "whisper-tiny-onnx" ],
                RootFolder: "unsafe-asr",
                BenchmarkEntry: "onnx/encoder_model.onnx",
                Variants:
                [
                    new ManifestVariantSpec("fp16", "onnx/encoder_model_fp16.onnx"),
                    new ManifestVariantSpec("int8", "onnx/encoder_model_int8.onnx")
                ]));

        RuntimePlanner planner = CreatePlanner(
            registry,
            [],
            [ new(ExecutionProviderKind.DirectMl, true) ],
            _ => new ExecutionProviderSmokeTestResult(true));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Asr, CommercialSafeMode: true));

        Assert.Equal(StageRuntimePlanStatus.Blocked, plan.Status);
        Assert.Null(plan.ModelId);
        Assert.NotNull(plan.Fallback);
        Assert.Equal(RuntimePlanFallbackCode.CommercialSafeExcluded, plan.Fallback!.Code);
        Assert.Contains(plan.Warnings, warning => warning.Code == RuntimePlanWarningCode.CommercialSafeModeActive);
    }

    [Fact]
    public async Task PlanAsync_CurrentBundledAsrCommercialSafeMode_ReturnsBlocked()
    {
        // This test intentionally validates the current production bundled manifest so Milestone 6
        // does not assume commercial-safe ASR is already available in the shipped inventory.
        BundledModelManifestRegistry registry = LoadBundledRegistry();
        RuntimePlanner planner = CreatePlanner(
            registry,
            [],
            [ new(ExecutionProviderKind.DirectMl, true) ],
            _ => new ExecutionProviderSmokeTestResult(true));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Asr, CommercialSafeMode: true));

        Assert.Equal(StageRuntimePlanStatus.Blocked, plan.Status);
        Assert.Null(plan.ModelId);
        Assert.NotNull(plan.Fallback);
        Assert.Equal(RuntimePlanFallbackCode.CommercialSafeExcluded, plan.Fallback!.Code);
        Assert.Contains(plan.Warnings, warning => warning.Code == RuntimePlanWarningCode.CommercialSafeModeActive);
    }

    [Fact]
    public async Task PlanAsync_CurrentBundledVadCommercialSafeMode_RemainsRunnable()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = LoadBundledRegistry();

        string cacheRoot = workspace.CreateCacheRoot("onnx-community/silero-vad");
        workspace.WriteCacheFile(cacheRoot, "onnx/model_int8.onnx");

        RuntimePlanner planner = CreatePlanner(
            registry,
            [ new("onnx-community/silero-vad", cacheRoot, "main", "sha", DateTimeOffset.UtcNow) ],
            [ new(ExecutionProviderKind.DirectMl, false, "No DirectML adapter found.") ],
            _ => throw new InvalidOperationException("Smoke tests should not run for CPU-only plans."));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: true));

        Assert.Equal(StageRuntimePlanStatus.Ready, plan.Status);
        Assert.Equal("onnx-community/silero-vad", plan.ModelId);
        Assert.Equal(ExecutionProviderKind.Cpu, plan.ExecutionProvider);
        Assert.Contains(plan.Warnings, warning => warning.Code == RuntimePlanWarningCode.CommercialSafeModeActive);
    }

    [Fact]
    public async Task PlanAsync_NonCpuProviderNeverReturnsReadyWithoutPassingSmokeTest()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateVadSpec("silero-vad", commercialAllowed: true, license: "MIT"));

        string cacheRoot = workspace.CreateCacheRoot("onnx-community/silero-vad");
        workspace.WriteCacheFile(cacheRoot, "onnx/model_fp16.onnx");

        RuntimePlanner planner = CreatePlanner(
            registry,
            [ new("onnx-community/silero-vad", cacheRoot, "main", "sha", DateTimeOffset.UtcNow) ],
            [ new(ExecutionProviderKind.DirectMl, true) ],
            _ => new ExecutionProviderSmokeTestResult(false, "Smoke-test failure is expected."));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: false));

        Assert.Equal(StageRuntimePlanStatus.Ready, plan.Status);
        Assert.Equal(ExecutionProviderKind.Cpu, plan.ExecutionProvider);
        Assert.NotEqual(ExecutionProviderKind.DirectMl, plan.ExecutionProvider);
        Assert.NotNull(plan.Fallback);
        Assert.Equal(RuntimePlanFallbackCode.ProviderSmokeTestFailed, plan.Fallback!.Code);
    }

    [Fact]
    public async Task StageRuntimePlan_RoundTripsWithoutLeakingMachinePaths()
    {
        using var workspace = new RuntimePlannerTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateVadSpec("silero-vad", commercialAllowed: true, license: "MIT"));

        string marker = $"machine-marker-{Guid.NewGuid():N}";
        string cacheRoot = workspace.CreateCacheRoot(Path.Combine(marker, "onnx-community-silero-vad"));
        workspace.WriteCacheFile(cacheRoot, "onnx/model_int8.onnx");

        RuntimePlanner planner = CreatePlanner(
            registry,
            [ new("onnx-community/silero-vad", cacheRoot, "main", "sha", DateTimeOffset.UtcNow) ],
            [ new(ExecutionProviderKind.DirectMl, false, "No DirectML adapter found.") ],
            _ => throw new InvalidOperationException("Smoke tests should not run for CPU-only plans."));

        StageRuntimePlan plan = await planner.PlanAsync(new StageRuntimePlanningRequest(RuntimeStage.Vad, CommercialSafeMode: true));
        string json = JsonSerializer.Serialize(plan);
        StageRuntimePlan? roundTripped = JsonSerializer.Deserialize<StageRuntimePlan>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(plan.Stage, roundTripped!.Stage);
        Assert.Equal(plan.Status, roundTripped.Status);
        Assert.Equal(plan.ModelId, roundTripped.ModelId);
        Assert.Equal(plan.ExecutionProvider, roundTripped.ExecutionProvider);
        Assert.Equal(plan.Warnings.Select(warning => warning.Code), roundTripped.Warnings.Select(warning => warning.Code));
        Assert.DoesNotContain(marker, json, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelRootPath", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalModelCacheInventory_ReadsMachineLocalCacheIndex()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), $"babelstudio-cache-{Guid.NewGuid():N}");
        var storagePaths = new BabelStudioStoragePaths(rootPath);
        var store = new LocalModelCacheRecordStore(storagePaths);
        var inventory = new LocalModelCacheInventory(store);
        LocalModelCacheRecord[] records =
        [
            new("example/model", Path.Combine(rootPath, "machine-cache", "example"), "main", "abc123", DateTimeOffset.UtcNow)
        ];

        try
        {
            await store.SaveAsync(records);
            IReadOnlyList<LocalModelCacheRecord> loaded = await inventory.LoadAsync();

            LocalModelCacheRecord record = Assert.Single(loaded);
            Assert.Equal(records[0].ModelId, record.ModelId);
            Assert.Equal(records[0].RootPath, record.RootPath);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static RuntimePlanner CreatePlanner(
        BundledModelManifestRegistry registry,
        IReadOnlyList<LocalModelCacheRecord> cacheRecords,
        IReadOnlyList<ExecutionProviderAvailability> availabilities,
        Func<ExecutionProviderSmokeTestRequest, ExecutionProviderSmokeTestResult> smokeHandler)
    {
        return new RuntimePlanner(
            registry,
            new CommercialSafeEvaluator(),
            new FakeHardwareProfileProvider(),
            new FakeExecutionProviderDiscovery(availabilities),
            new FakeExecutionProviderSmokeTester(smokeHandler),
            new InMemoryModelCacheInventory(cacheRecords));
    }

    private static BundledModelManifestRegistry LoadBundledRegistry()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("BUNDLED_MANIFEST_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return BundledModelManifestRegistry.Load(configuredPath);
        }

        try
        {
            string repoRoot = FindRepoRoot();
            string manifestPath = Path.Combine(
                repoRoot,
                "src",
                "BabelStudio.Inference",
                "Runtime",
                "ModelManifest",
                "bundled-models.manifest.json");

            if (File.Exists(manifestPath))
            {
                return BundledModelManifestRegistry.Load(manifestPath);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Fall through to try assembly-relative path resolution.
        }

        string assemblyRelativePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "BabelStudio.Inference",
            "Runtime",
            "ModelManifest",
            "bundled-models.manifest.json"));

        if (File.Exists(assemblyRelativePath))
        {
            return BundledModelManifestRegistry.Load(assemblyRelativePath);
        }

        throw new FileNotFoundException("Could not locate bundled-models.manifest.json for runtime planner tests.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BabelStudio.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static ManifestSpec CreateVadSpec(
        string primaryAlias,
        bool commercialAllowed,
        string license,
        string? modelId = null) =>
        new(
            ModelId: modelId ?? "onnx-community/silero-vad",
            Task: "vad",
            License: license,
            CommercialAllowed: commercialAllowed,
            RequiresAttribution: false,
            RequiresUserConsent: false,
            VoiceCloning: false,
            Aliases: primaryAlias.Equals("silero", StringComparison.OrdinalIgnoreCase)
                ? [ "silero" ]
                : [ "silero-vad", "silero" ],
            RootFolder: primaryAlias.Replace('/', '-'),
            BenchmarkEntry: "onnx/model.onnx",
            Variants:
            [
                new ManifestVariantSpec("fp16", "onnx/model_fp16.onnx"),
                new ManifestVariantSpec("int8", "onnx/model_int8.onnx"),
                new ManifestVariantSpec("q4f16", "onnx/model_q4f16.onnx"),
                new ManifestVariantSpec("quantized", "onnx/model_quantized.onnx"),
                new ManifestVariantSpec("uint8", "onnx/model_uint8.onnx"),
                new ManifestVariantSpec("q4", "onnx/model_q4.onnx")
            ]);

    private sealed class RuntimePlannerTestWorkspace : IDisposable
    {
        public RuntimePlannerTestWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"babelstudio-runtime-planner-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public BundledModelManifestRegistry WriteManifest(params ManifestSpec[] models)
        {
            string manifestPath = Path.Combine(RootPath, "bundled-models.manifest.json");
            string json = JsonSerializer.Serialize(
                new
                {
                    models = models.Select(model => new
                    {
                        model_id = model.ModelId,
                        task = model.Task,
                        license = model.License,
                        commercial_allowed = model.CommercialAllowed,
                        redistribution_allowed = true,
                        requires_attribution = model.RequiresAttribution,
                        requires_user_consent = model.RequiresUserConsent,
                        voice_cloning = model.VoiceCloning,
                        commercial_safe_mode = model.CommercialAllowed &&
                                               !model.License.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                                               !model.License.Equals("non-commercial", StringComparison.OrdinalIgnoreCase),
                        source_url = $"https://example.invalid/{model.ModelId.Replace('/', '-')}",
                        revision = "main",
                        sha256 = "sha",
                        aliases = model.Aliases,
                        root_path = $"./manifest-models/{model.RootFolder}",
                        benchmark_entry = model.BenchmarkEntry,
                        variants = model.Variants.Select(variant => new
                        {
                            alias = variant.Alias,
                            entry_path = variant.EntryPath
                        })
                    })
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(manifestPath, json);
            return BundledModelManifestRegistry.Load(manifestPath);
        }

        public string CreateCacheRoot(string name)
        {
            string cacheRoot = Path.Combine(RootPath, "machine-cache", name);
            Directory.CreateDirectory(cacheRoot);
            return cacheRoot;
        }

        public void WriteCacheFile(string cacheRoot, string relativePath)
        {
            string filePath = Path.Combine(cacheRoot, relativePath);
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, "placeholder");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp directories created by planner tests.
            }
        }
    }

    private sealed class FakeHardwareProfileProvider : IHardwareProfileProvider
    {
        public Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareProfile("windows", "x64", HasGpu: true, GpuDescription: "Test GPU"));
    }

    private sealed class FakeExecutionProviderDiscovery : IExecutionProviderDiscovery
    {
        private readonly IReadOnlyList<ExecutionProviderAvailability> availabilities;

        public FakeExecutionProviderDiscovery(IReadOnlyList<ExecutionProviderAvailability> availabilities)
        {
            this.availabilities = availabilities;
        }

        public Task<IReadOnlyList<ExecutionProviderAvailability>> DiscoverAsync(
            HardwareProfile hardwareProfile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(availabilities);
    }

    private sealed class FakeExecutionProviderSmokeTester : IExecutionProviderSmokeTester
    {
        private readonly Func<ExecutionProviderSmokeTestRequest, ExecutionProviderSmokeTestResult> handler;

        public FakeExecutionProviderSmokeTester(Func<ExecutionProviderSmokeTestRequest, ExecutionProviderSmokeTestResult> handler)
        {
            this.handler = handler;
        }

        public Task<ExecutionProviderSmokeTestResult> SmokeTestAsync(
            ExecutionProviderSmokeTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(handler(request));
    }

    private sealed class InMemoryModelCacheInventory : IModelCacheInventory
    {
        private readonly IReadOnlyList<LocalModelCacheRecord> records;

        public InMemoryModelCacheInventory(IReadOnlyList<LocalModelCacheRecord> records)
        {
            this.records = records;
        }

        public Task<IReadOnlyList<LocalModelCacheRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(records);
    }

    private sealed record ManifestSpec(
        string ModelId,
        string Task,
        string License,
        bool CommercialAllowed,
        bool RequiresAttribution,
        bool RequiresUserConsent,
        bool VoiceCloning,
        IReadOnlyList<string> Aliases,
        string RootFolder,
        string BenchmarkEntry,
        IReadOnlyList<ManifestVariantSpec> Variants);

    private sealed record ManifestVariantSpec(
        string Alias,
        string EntryPath);
}
