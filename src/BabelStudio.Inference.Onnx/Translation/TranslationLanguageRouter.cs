using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Inference.Onnx.Translation;

public sealed class TranslationLanguageRouter : ITranslationLanguageRouter
{
    private const string DirectOpusProviderName = "opus-mt";
    private const string PivotMadladProviderName = "madlad400";
    private readonly BundledModelManifestRegistry manifestRegistry;
    private readonly IModelCacheInventory modelCacheInventory;
    private readonly CommercialSafeEvaluator commercialSafeEvaluator;

    public TranslationLanguageRouter(
        BundledModelManifestRegistry manifestRegistry,
        IModelCacheInventory modelCacheInventory,
        CommercialSafeEvaluator commercialSafeEvaluator)
    {
        this.manifestRegistry = manifestRegistry ?? throw new ArgumentNullException(nameof(manifestRegistry));
        this.modelCacheInventory = modelCacheInventory ?? throw new ArgumentNullException(nameof(modelCacheInventory));
        this.commercialSafeEvaluator = commercialSafeEvaluator ?? throw new ArgumentNullException(nameof(commercialSafeEvaluator));
    }

    public async Task<IReadOnlyList<TranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
        string sourceLanguage,
        bool commercialSafeMode,
        CancellationToken cancellationToken)
    {
        TranslationRoutingContext context = await BuildContextAsync(commercialSafeMode, cancellationToken).ConfigureAwait(false);
        return TranslationLanguageCoverageMatrix.GetTargets(sourceLanguage)
            .Select(language =>
            {
                TranslationRouteSelection route = ResolveRoute(
                    context,
                    sourceLanguage,
                    language.Code,
                    language.DisplayName);
                return new TranslationTargetLanguageOption(
                    language.Code,
                    language.DisplayName,
                    route.RoutingKind,
                    route.IsAvailable,
                    route.IsAvailable
                        ? route.RouteDetail
                        : route.UnavailableReason ?? route.RouteDetail);
            })
            .ToArray();
    }

    public async Task<TranslationRouteSelection> ResolveRouteAsync(
        string sourceLanguage,
        string targetLanguage,
        bool commercialSafeMode,
        CancellationToken cancellationToken)
    {
        TranslationRoutingContext context = await BuildContextAsync(commercialSafeMode, cancellationToken).ConfigureAwait(false);
        return ResolveRoute(context, sourceLanguage, targetLanguage, displayName: null);
    }

    private async Task<TranslationRoutingContext> BuildContextAsync(
        bool commercialSafeMode,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelCacheRecord> cacheRecords = await modelCacheInventory.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, LocalModelCacheRecord> cacheIndex = cacheRecords
            .GroupBy(record => record.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(record => record.CachedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<(string SourceLanguage, string TargetLanguage), BundledModelManifestEntry> opusEntriesByPair = manifestRegistry.Entries
            .Where(static entry => string.Equals(entry.Task, "translation", StringComparison.OrdinalIgnoreCase))
            .Select(entry => (Entry: entry, Pair: TryParseDirectOpusPair(entry)))
            .Where(candidate => candidate.Pair is not null)
            .ToDictionary(
                candidate => candidate.Pair!.Value,
                candidate => candidate.Entry);

        BundledModelManifestEntry? madladEntry = manifestRegistry.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Task, "translation", StringComparison.OrdinalIgnoreCase) &&
            entry.Aliases.Any(alias => string.Equals(alias, "madlad400-mt", StringComparison.OrdinalIgnoreCase)));

        return new TranslationRoutingContext(commercialSafeMode, cacheIndex, opusEntriesByPair, madladEntry);
    }

    private TranslationRouteSelection ResolveRoute(
        TranslationRoutingContext context,
        string sourceLanguage,
        string targetLanguage,
        string? displayName)
    {
        string normalizedSourceLanguage = NormalizeLanguageCode(sourceLanguage)
            ?? throw new InvalidOperationException("Source language is required.");
        string normalizedTargetLanguage = NormalizeLanguageCode(targetLanguage)
            ?? throw new InvalidOperationException("Target language is required.");

        if (!TranslationLanguageCoverageMatrix.TryGetLanguage(normalizedTargetLanguage, out TranslationLanguageDefinition? targetDefinition) ||
            !TranslationLanguageCoverageMatrix.GetTargets(normalizedSourceLanguage)
                .Any(candidate => string.Equals(candidate.Code, normalizedTargetLanguage, StringComparison.Ordinal)))
        {
            return new TranslationRouteSelection(
                normalizedSourceLanguage,
                normalizedTargetLanguage,
                TranslationRoutingKind.Unavailable,
                IsAvailable: false,
                ProviderName: "none",
                RouteDetail: "Unavailable",
                UnavailableReason: $"Milestone 9 coverage does not include {normalizedSourceLanguage} -> {normalizedTargetLanguage}.");
        }

        string resolvedDisplayName = displayName ?? targetDefinition!.DisplayName;
        if (TryResolveDirectOpusRoute(context, normalizedSourceLanguage, normalizedTargetLanguage, out TranslationRouteSelection? directRoute))
        {
            return directRoute!;
        }

        if (TryResolveMadladPivotRoute(context, normalizedSourceLanguage, normalizedTargetLanguage, out TranslationRouteSelection? pivotRoute))
        {
            return pivotRoute!;
        }

        string missingReason = context.OpusEntriesByPair.ContainsKey((normalizedSourceLanguage, normalizedTargetLanguage))
            ? $"Direct Opus-MT is not installed for {resolvedDisplayName}, and MADLAD-400 pivot is unavailable."
            : $"MADLAD-400 pivot is unavailable for {resolvedDisplayName}.";

        return new TranslationRouteSelection(
            normalizedSourceLanguage,
            normalizedTargetLanguage,
            TranslationRoutingKind.Unavailable,
            IsAvailable: false,
            ProviderName: "none",
            RouteDetail: "Unavailable",
            UnavailableReason: missingReason);
    }

    private bool TryResolveDirectOpusRoute(
        TranslationRoutingContext context,
        string sourceLanguage,
        string targetLanguage,
        out TranslationRouteSelection? route)
    {
        route = null;
        if (!context.OpusEntriesByPair.TryGetValue((sourceLanguage, targetLanguage), out BundledModelManifestEntry? entry))
        {
            return false;
        }

        if (!IsEligible(entry, context.CommercialSafeMode))
        {
            return false;
        }

        if (!TryResolveEntryPath(context.CacheIndex, entry, out string? entryPath))
        {
            return false;
        }

        string modelRootPath = Path.GetDirectoryName(entryPath)
            ?? throw new InvalidOperationException("Direct Opus model root path could not be resolved.");
        if (!HasOpusSupportingFiles(modelRootPath))
        {
            return false;
        }

        route = new TranslationRouteSelection(
            sourceLanguage,
            targetLanguage,
            TranslationRoutingKind.Direct,
            IsAvailable: true,
            ProviderName: DirectOpusProviderName,
            RouteDetail: "Direct Opus-MT",
            ModelId: entry.ModelId,
            PreferredModelAlias: entry.Aliases.FirstOrDefault(),
            ResolvedModelEntryPath: entryPath);
        return true;
    }

    private bool TryResolveMadladPivotRoute(
        TranslationRoutingContext context,
        string sourceLanguage,
        string targetLanguage,
        out TranslationRouteSelection? route)
    {
        route = null;
        BundledModelManifestEntry? entry = context.MadladEntry;
        if (entry is null || !IsEligible(entry, context.CommercialSafeMode))
        {
            return false;
        }

        if (!TryResolveMadladEntryPath(context.CacheIndex, entry, out string? entryPath))
        {
            return false;
        }

        string modelRootPath = Path.GetDirectoryName(entryPath)
            ?? throw new InvalidOperationException("MADLAD model root path could not be resolved.");
        if (!HasMadladSupportingFiles(modelRootPath))
        {
            return false;
        }

        string routeDetail = context.OpusEntriesByPair.ContainsKey((sourceLanguage, targetLanguage))
            ? "MADLAD-400 pivot (direct Opus-MT pair missing)"
            : "MADLAD-400 pivot";

        route = new TranslationRouteSelection(
            sourceLanguage,
            targetLanguage,
            TranslationRoutingKind.Pivot,
            IsAvailable: true,
            ProviderName: PivotMadladProviderName,
            RouteDetail: routeDetail,
            ModelId: entry.ModelId,
            PreferredModelAlias: entry.Aliases.FirstOrDefault(),
            ResolvedModelEntryPath: entryPath);
        return true;
    }

    private bool IsEligible(BundledModelManifestEntry entry, bool commercialSafeMode)
    {
        if (!commercialSafeMode)
        {
            return true;
        }

        return commercialSafeEvaluator.Evaluate(entry).IsCommercialSafe;
    }

    private static bool TryResolveEntryPath(
        IReadOnlyDictionary<string, LocalModelCacheRecord> cacheIndex,
        BundledModelManifestEntry entry,
        out string? entryPath)
    {
        entryPath = null;
        if (!cacheIndex.TryGetValue(entry.ModelId, out LocalModelCacheRecord? record))
        {
            return false;
        }

        string relativeEntryPath = Path.GetRelativePath(entry.RootDirectory, entry.DefaultBenchmarkEntryPath);
        string resolvedPath = Path.GetFullPath(Path.Combine(record.RootPath, relativeEntryPath));
        if (!File.Exists(resolvedPath))
        {
            return false;
        }

        entryPath = resolvedPath;
        return true;
    }

    private static bool TryResolveMadladEntryPath(
        IReadOnlyDictionary<string, LocalModelCacheRecord> cacheIndex,
        BundledModelManifestEntry entry,
        out string? entryPath)
    {
        entryPath = null;
        if (!cacheIndex.TryGetValue(entry.ModelId, out LocalModelCacheRecord? record))
        {
            return false;
        }

        foreach (string candidatePath in EnumerateMadladEncoderEntryPaths(entry))
        {
            string relativeEntryPath = Path.GetRelativePath(entry.RootDirectory, candidatePath);
            string resolvedPath = Path.GetFullPath(Path.Combine(record.RootPath, relativeEntryPath));
            if (File.Exists(resolvedPath))
            {
                entryPath = resolvedPath;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateMadladEncoderEntryPaths(BundledModelManifestEntry entry)
    {
        yield return entry.DefaultBenchmarkEntryPath;

        foreach (BundledModelManifestVariant variant in entry.Variants)
        {
            string fileName = Path.GetFileName(variant.EntryPath);
            if (fileName.StartsWith("encoder_model", StringComparison.OrdinalIgnoreCase))
            {
                yield return variant.EntryPath;
            }
        }
    }

    private static bool HasOpusSupportingFiles(string modelRootPath)
    {
        return (File.Exists(Path.Combine(modelRootPath, "decoder_model.onnx")) ||
                File.Exists(Path.Combine(modelRootPath, "decoder_model_merged.onnx"))) &&
               File.Exists(Path.Combine(modelRootPath, "vocab.json")) &&
               (File.Exists(Path.Combine(modelRootPath, "source.spm")) || File.Exists(Path.Combine(modelRootPath, "source.model"))) &&
               (File.Exists(Path.Combine(modelRootPath, "target.spm")) || File.Exists(Path.Combine(modelRootPath, "target.model")));
    }

    private static bool HasMadladSupportingFiles(string modelRootPath)
    {
        return (File.Exists(Path.Combine(modelRootPath, "decoder_model.onnx")) ||
                File.Exists(Path.Combine(modelRootPath, "decoder_model_merged.onnx")) ||
                File.Exists(Path.Combine(modelRootPath, "decoder_model_int8.onnx"))) &&
               (File.Exists(Path.Combine(modelRootPath, "spiece.model")) ||
                File.Exists(Path.Combine(modelRootPath, "tokenizer.model")) ||
                File.Exists(Path.Combine(modelRootPath, "sentencepiece.model")));
    }

    private static (string SourceLanguage, string TargetLanguage)? TryParseDirectOpusPair(BundledModelManifestEntry entry)
    {
        foreach (string alias in entry.Aliases)
        {
            if (!alias.StartsWith("opus-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = alias.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3)
            {
                return (parts[1].ToLowerInvariant(), parts[2].ToLowerInvariant());
            }
        }

        return null;
    }

    private static string? NormalizeLanguageCode(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? null
            : languageCode.Trim().ToLowerInvariant();

    private sealed record TranslationRoutingContext(
        bool CommercialSafeMode,
        IReadOnlyDictionary<string, LocalModelCacheRecord> CacheIndex,
        IReadOnlyDictionary<(string SourceLanguage, string TargetLanguage), BundledModelManifestEntry> OpusEntriesByPair,
        BundledModelManifestEntry? MadladEntry);
}
