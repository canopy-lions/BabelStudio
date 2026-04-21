using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.ModelManifest;
using System.Collections.Concurrent;

namespace BabelStudio.Inference.Runtime.Planning;

public sealed class RuntimePlanner : IRuntimePlanner
{
    private readonly BundledModelManifestRegistry manifestRegistry;
    private readonly CommercialSafeEvaluator commercialSafeEvaluator;
    private readonly IHardwareProfileProvider hardwareProfileProvider;
    private readonly IExecutionProviderDiscovery executionProviderDiscovery;
    private readonly IExecutionProviderSmokeTester executionProviderSmokeTester;
    private readonly IModelCacheInventory modelCacheInventory;
    private readonly IReadOnlyDictionary<RuntimeStage, StageRuntimeRequirements> stageRequirements;

    public RuntimePlanner(
        BundledModelManifestRegistry manifestRegistry,
        CommercialSafeEvaluator commercialSafeEvaluator,
        IHardwareProfileProvider hardwareProfileProvider,
        IExecutionProviderDiscovery executionProviderDiscovery,
        IExecutionProviderSmokeTester executionProviderSmokeTester,
        IModelCacheInventory modelCacheInventory,
        IReadOnlyDictionary<RuntimeStage, StageRuntimeRequirements>? stageRequirements = null)
    {
        this.manifestRegistry = manifestRegistry ?? throw new ArgumentNullException(nameof(manifestRegistry));
        this.commercialSafeEvaluator = commercialSafeEvaluator ?? throw new ArgumentNullException(nameof(commercialSafeEvaluator));
        this.hardwareProfileProvider = hardwareProfileProvider ?? throw new ArgumentNullException(nameof(hardwareProfileProvider));
        this.executionProviderDiscovery = executionProviderDiscovery ?? throw new ArgumentNullException(nameof(executionProviderDiscovery));
        this.executionProviderSmokeTester = executionProviderSmokeTester ?? throw new ArgumentNullException(nameof(executionProviderSmokeTester));
        this.modelCacheInventory = modelCacheInventory ?? throw new ArgumentNullException(nameof(modelCacheInventory));
        this.stageRequirements = stageRequirements ?? Milestone5StageRuntimeRequirementsCatalog.All;
    }

    public async Task<StageRuntimePlan> PlanAsync(
        StageRuntimePlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fileExistenceCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (!stageRequirements.TryGetValue(request.Stage, out StageRuntimeRequirements? requirements))
        {
            throw new InvalidOperationException($"Runtime stage '{request.Stage}' is not configured for milestone 5 planning.");
        }

        HardwareProfile hardwareProfile = await hardwareProfileProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ExecutionProviderAvailability> providerAvailabilities = await executionProviderDiscovery.DiscoverAsync(
            hardwareProfile,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, LocalModelCacheRecord> cacheIndex = await BuildCacheIndexAsync(cancellationToken).ConfigureAwait(false);

        RankedManifestEntry[] rankedEntries = RankEntries(request, requirements);
        RankedManifestEntry[] filteredEntries = FilterCommercialSafeEntries(request, rankedEntries);
        if (filteredEntries.Length == 0)
        {
            return CreateBlockedPlan(
                request.Stage,
                request.CommercialSafeMode,
                new RuntimePlanFallback(
                    RuntimePlanFallbackCode.CommercialSafeExcluded,
                    $"No {requirements.RequiredTask.ToManifestValue()} model remains eligible after commercial-safe filtering."));
        }

        foreach (RankedManifestEntry candidate in filteredEntries)
        {
            StageRuntimePlan? readyPlan = await TryCreateReadyPlanAsync(
                request.Stage,
                request.CommercialSafeMode,
                requirements,
                candidate,
                providerAvailabilities,
                cacheIndex,
                fileExistenceCache,
                cancellationToken).ConfigureAwait(false);

            if (readyPlan is not null)
            {
                return readyPlan;
            }
        }

        foreach (RankedManifestEntry candidate in filteredEntries)
        {
            StageRuntimePlan? downloadPlan = TryCreateDownloadRequiredPlan(
                request.Stage,
                request.CommercialSafeMode,
                requirements,
                candidate,
                providerAvailabilities,
                cacheIndex,
                fileExistenceCache);

            if (downloadPlan is not null)
            {
                return downloadPlan;
            }
        }

        return CreateBlockedPlan(
            request.Stage,
            request.CommercialSafeMode,
            new RuntimePlanFallback(
                RuntimePlanFallbackCode.NoCompatibleVariant,
                $"No compatible {requirements.RequiredTask.ToManifestValue()} variant could be planned for milestone 5 providers."));
    }

    private RankedManifestEntry[] RankEntries(
        StageRuntimePlanningRequest request,
        StageRuntimeRequirements requirements)
    {
        return manifestRegistry.Entries
            .Where(entry => string.Equals(entry.Task, requirements.RequiredTask.ToManifestValue(), StringComparison.OrdinalIgnoreCase))
            .Select(entry => new RankedManifestEntry(
                entry,
                commercialSafeEvaluator.Evaluate(entry),
                GetAliasRank(entry, request, requirements)))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Entry.Aliases.FirstOrDefault() ?? candidate.Entry.ModelId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Entry.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RankedManifestEntry[] FilterCommercialSafeEntries(
        StageRuntimePlanningRequest request,
        IReadOnlyList<RankedManifestEntry> rankedEntries)
    {
        if (!request.CommercialSafeMode)
        {
            return rankedEntries.ToArray();
        }

        return rankedEntries
            .Where(candidate => candidate.CommercialSafe.IsCommercialSafe)
            .ToArray();
    }

    private async Task<StageRuntimePlan?> TryCreateReadyPlanAsync(
        RuntimeStage stage,
        bool commercialSafeMode,
        StageRuntimeRequirements requirements,
        RankedManifestEntry candidate,
        IReadOnlyList<ExecutionProviderAvailability> providerAvailabilities,
        IReadOnlyDictionary<string, LocalModelCacheRecord> cacheIndex,
        ConcurrentDictionary<string, bool> fileExistenceCache,
        CancellationToken cancellationToken)
    {
        cacheIndex.TryGetValue(candidate.Entry.ModelId, out LocalModelCacheRecord? cacheRecord);

        RuntimePlanFallback? providerFallback = null;
        foreach (ExecutionProviderKind provider in GetOrderedProviders(requirements))
        {
            ExecutionProviderAvailability availability = GetAvailability(providerAvailabilities, provider);
            if (provider is not ExecutionProviderKind.Cpu && !availability.IsAvailable)
            {
                providerFallback ??= new RuntimePlanFallback(
                    RuntimePlanFallbackCode.ProviderUnavailable,
                    availability.Detail ?? $"{provider} is not available on this machine.");
                continue;
            }

            foreach (VariantCandidate variant in EnumerateVariants(candidate.Entry, provider, requirements))
            {
                string? entryPath = ResolveCachedEntryPath(cacheRecord, variant);
                if (entryPath is null || !FileExists(fileExistenceCache, entryPath))
                {
                    continue;
                }

                if (provider is ExecutionProviderKind.Cpu)
                {
                    return CreatePlan(
                        stage,
                        StageRuntimePlanStatus.Ready,
                        candidate,
                        provider,
                        variant.Alias,
                        commercialSafeMode,
                        providerFallback,
                    includeCpuFallbackWarning: providerFallback is not null);
                }

                if (cacheRecord is null)
                {
                    continue;
                }

                string rootPath = cacheRecord.RootPath;
                ExecutionProviderSmokeTestResult smokeResult;
                try
                {
                    smokeResult = await executionProviderSmokeTester.SmokeTestAsync(
                        new ExecutionProviderSmokeTestRequest(
                            stage,
                            candidate.Entry.ModelId,
                            ResolvePrimaryAlias(candidate.Entry),
                            variant.Alias,
                            provider,
                            rootPath,
                            entryPath),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    smokeResult = new ExecutionProviderSmokeTestResult(false, ex.Message);
                }

                if (smokeResult.Passed)
                {
                    return CreatePlan(
                        stage,
                        StageRuntimePlanStatus.Ready,
                        candidate,
                        provider,
                        variant.Alias,
                        commercialSafeMode,
                        fallback: null,
                        includeCpuFallbackWarning: false);
                }

                providerFallback ??= new RuntimePlanFallback(
                    RuntimePlanFallbackCode.ProviderSmokeTestFailed,
                    smokeResult.Detail ?? $"{provider} smoke test failed for variant '{variant.Alias}'.");
            }
        }

        return null;
    }

    private StageRuntimePlan? TryCreateDownloadRequiredPlan(
        RuntimeStage stage,
        bool commercialSafeMode,
        StageRuntimeRequirements requirements,
        RankedManifestEntry candidate,
        IReadOnlyList<ExecutionProviderAvailability> providerAvailabilities,
        IReadOnlyDictionary<string, LocalModelCacheRecord> cacheIndex,
        ConcurrentDictionary<string, bool> fileExistenceCache)
    {
        cacheIndex.TryGetValue(candidate.Entry.ModelId, out LocalModelCacheRecord? cacheRecord);

        foreach (ExecutionProviderKind provider in GetOrderedProviders(requirements))
        {
            ExecutionProviderAvailability availability = GetAvailability(providerAvailabilities, provider);
            if (provider is not ExecutionProviderKind.Cpu && !availability.IsAvailable)
            {
                continue;
            }

            foreach (VariantCandidate variant in EnumerateVariants(candidate.Entry, provider, requirements))
            {
                string? entryPath = ResolveCachedEntryPath(cacheRecord, variant);
                if (entryPath is not null && FileExists(fileExistenceCache, entryPath))
                {
                    continue;
                }

                return CreatePlan(
                    stage,
                    StageRuntimePlanStatus.DownloadRequired,
                    candidate,
                    provider,
                    variant.Alias,
                    commercialSafeMode,
                    new RuntimePlanFallback(
                        RuntimePlanFallbackCode.ModelNotCached,
                        $"Machine-local cache does not contain '{variant.RelativeEntryPath}' for model '{candidate.Entry.ModelId}'."),
                    includeCpuFallbackWarning: false);
            }
        }

        return null;
    }

    private static StageRuntimePlan CreatePlan(
        RuntimeStage stage,
        StageRuntimePlanStatus status,
        RankedManifestEntry candidate,
        ExecutionProviderKind provider,
        string variant,
        bool commercialSafeMode,
        RuntimePlanFallback? fallback,
        bool includeCpuFallbackWarning)
    {
        return new StageRuntimePlan
        {
            Stage = stage,
            Status = status,
            ModelId = candidate.Entry.ModelId,
            ModelAlias = ResolvePrimaryAlias(candidate.Entry),
            Variant = variant,
            ExecutionProvider = provider,
            Fallback = fallback,
            Warnings = BuildWarnings(candidate.CommercialSafe, commercialSafeMode, includeCpuFallbackWarning)
        };
    }

    private static StageRuntimePlan CreateBlockedPlan(
        RuntimeStage stage,
        bool commercialSafeMode,
        RuntimePlanFallback fallback)
    {
        return new StageRuntimePlan
        {
            Stage = stage,
            Status = StageRuntimePlanStatus.Blocked,
            Fallback = fallback,
            Warnings = commercialSafeMode
                ? [ new RuntimePlanWarning(RuntimePlanWarningCode.CommercialSafeModeActive) ]
                : []
        };
    }

    private static IReadOnlyList<RuntimePlanWarning> BuildWarnings(
        CommercialSafeEvaluation commercialSafe,
        bool commercialSafeMode,
        bool includeCpuFallbackWarning)
    {
        var warnings = new List<RuntimePlanWarning>();
        if (commercialSafeMode)
        {
            warnings.Add(new RuntimePlanWarning(RuntimePlanWarningCode.CommercialSafeModeActive));
        }

        if (includeCpuFallbackWarning)
        {
            warnings.Add(new RuntimePlanWarning(RuntimePlanWarningCode.CpuFallback));
        }

        if (commercialSafe.RequiresAttribution)
        {
            warnings.Add(new RuntimePlanWarning(RuntimePlanWarningCode.AttributionRequired));
        }

        if (commercialSafe.RequiresUserConsent)
        {
            warnings.Add(new RuntimePlanWarning(RuntimePlanWarningCode.UserConsentRequired));
        }

        return warnings;
    }

    private static IReadOnlyList<ExecutionProviderKind> GetOrderedProviders(StageRuntimeRequirements requirements)
    {
        return Milestone5PlanningPolicy.SupportedProvidersThisMilestone
            .Where(provider => requirements.AllowedProvidersThisMilestone.Contains(provider))
            .ToArray();
    }

    private static ExecutionProviderAvailability GetAvailability(
        IReadOnlyList<ExecutionProviderAvailability> providerAvailabilities,
        ExecutionProviderKind provider)
    {
        ExecutionProviderAvailability? availability = providerAvailabilities
            .FirstOrDefault(candidate => candidate.Provider == provider);

        if (availability is not null)
        {
            return availability;
        }

        return provider switch
        {
            ExecutionProviderKind.Cpu => new ExecutionProviderAvailability(provider, true),
            _ => new ExecutionProviderAvailability(provider, false, $"{provider} was not reported by execution provider discovery.")
        };
    }

    private static IReadOnlyList<VariantCandidate> EnumerateVariants(
        BundledModelManifestEntry entry,
        ExecutionProviderKind provider,
        StageRuntimeRequirements requirements)
    {
        var preferredAliases = provider switch
        {
            ExecutionProviderKind.DirectMl => requirements.PreferredGpuVariants,
            _ => requirements.PreferredCpuVariants
        };

        var candidates = new List<VariantCandidate>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variantsByAlias = entry.Variants.ToDictionary(variant => variant.Alias, StringComparer.OrdinalIgnoreCase);

        foreach (string alias in preferredAliases)
        {
            if (!variantsByAlias.TryGetValue(alias, out BundledModelManifestVariant? variant))
            {
                continue;
            }

            AddVariant(candidates, seenPaths, entry, alias, variant.EntryPath);
        }

        AddVariant(candidates, seenPaths, entry, "default", entry.DefaultBenchmarkEntryPath);

        foreach (BundledModelManifestVariant variant in entry.Variants.OrderBy(variant => variant.Alias, StringComparer.OrdinalIgnoreCase))
        {
            AddVariant(candidates, seenPaths, entry, variant.Alias, variant.EntryPath);
        }

        return candidates;
    }

    private static void AddVariant(
        ICollection<VariantCandidate> candidates,
        ISet<string> seenPaths,
        BundledModelManifestEntry entry,
        string alias,
        string absoluteEntryPath)
    {
        string relativeEntryPath = Path.GetRelativePath(entry.RootDirectory, absoluteEntryPath);
        if (!seenPaths.Add(relativeEntryPath))
        {
            return;
        }

        candidates.Add(new VariantCandidate(alias, relativeEntryPath));
    }

    private static string? ResolveCachedEntryPath(LocalModelCacheRecord? cacheRecord, VariantCandidate variant)
    {
        if (cacheRecord is null)
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(cacheRecord.RootPath, variant.RelativeEntryPath));
    }

    private static bool FileExists(ConcurrentDictionary<string, bool> fileExistenceCache, string path) =>
        fileExistenceCache.GetOrAdd(path, static candidatePath => File.Exists(candidatePath));

    private async Task<IReadOnlyDictionary<string, LocalModelCacheRecord>> BuildCacheIndexAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelCacheRecord> records = await modelCacheInventory.LoadAsync(cancellationToken).ConfigureAwait(false);
        return records
            .GroupBy(record => record.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(record => record.CachedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static int GetAliasRank(
        BundledModelManifestEntry entry,
        StageRuntimePlanningRequest request,
        StageRuntimeRequirements requirements)
    {
        int rankOffset = 0;
        int bestRank = int.MaxValue / 2;

        if (!string.IsNullOrWhiteSpace(request.PreferredModelAlias))
        {
            if (entry.Aliases.Any(alias => alias.Equals(request.PreferredModelAlias, StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }

            rankOffset = 1;
        }

        for (int index = 0; index < requirements.PreferredModelAliases.Count; index++)
        {
            string preferredAlias = requirements.PreferredModelAliases[index];
            if (entry.Aliases.Any(alias => alias.Equals(preferredAlias, StringComparison.OrdinalIgnoreCase)))
            {
                bestRank = Math.Min(bestRank, rankOffset + index + 1);
            }
        }

        return bestRank;
    }

    private static string ResolvePrimaryAlias(BundledModelManifestEntry entry) =>
        entry.Aliases.FirstOrDefault() ?? entry.ModelId;

    private sealed record RankedManifestEntry(
        BundledModelManifestEntry Entry,
        CommercialSafeEvaluation CommercialSafe,
        int Rank);

    private sealed record VariantCandidate(
        string Alias,
        string RelativeEntryPath);
}
