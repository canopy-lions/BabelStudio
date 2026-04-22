using System.Text.Json;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Inference.Onnx.Translation;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Inference.Tests;

public sealed class TranslationLanguageRouterTests
{
    [Fact]
    public async Task ResolveRouteAsync_WhenDirectOpusPairIsInstalled_SelectsDirectRoute()
    {
        using var workspace = new TranslationRouterTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateTranslationSpec(
                "Helsinki-NLP/opus-mt-en-es",
                [ "opus-en-es", "helsinki-opus-en-es" ],
                "manifest-models/opus-en-es"),
            CreateMadladSpec());
        string opusCacheRoot = workspace.CreateCacheRoot("Helsinki-NLP/opus-mt-en-es");
        workspace.WriteOpusCacheFiles(opusCacheRoot);
        string madladCacheRoot = workspace.CreateCacheRoot("google/madlad400-3b-mt");
        workspace.WriteMadladCacheFiles(madladCacheRoot);

        var router = new TranslationLanguageRouter(
            registry,
            new InMemoryModelCacheInventory(
            [
                new LocalModelCacheRecord("Helsinki-NLP/opus-mt-en-es", opusCacheRoot, "main", "sha", DateTimeOffset.UtcNow),
                new LocalModelCacheRecord("google/madlad400-3b-mt", madladCacheRoot, "main", "sha", DateTimeOffset.UtcNow)
            ]),
            new CommercialSafeEvaluator());

        TranslationRouteSelection route = await router.ResolveRouteAsync("en", "es", commercialSafeMode: false, CancellationToken.None);

        Assert.True(route.IsAvailable);
        Assert.Equal(TranslationRoutingKind.Direct, route.RoutingKind);
        Assert.Equal("opus-mt", route.ProviderName);
        Assert.Equal("Helsinki-NLP/opus-mt-en-es", route.ModelId);
        Assert.EndsWith("encoder_model.onnx", route.ResolvedModelEntryPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveRouteAsync_WhenDirectPairIsMissingAndMadladIsInstalled_SelectsPivotRoute()
    {
        using var workspace = new TranslationRouterTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateTranslationSpec(
                "Helsinki-NLP/opus-mt-en-es",
                [ "opus-en-es", "helsinki-opus-en-es" ],
                "manifest-models/opus-en-es"),
            CreateMadladSpec());
        string madladCacheRoot = workspace.CreateCacheRoot("google/madlad400-3b-mt");
        workspace.WriteMadladCacheFiles(madladCacheRoot);

        var router = new TranslationLanguageRouter(
            registry,
            new InMemoryModelCacheInventory(
            [
                new LocalModelCacheRecord("google/madlad400-3b-mt", madladCacheRoot, "main", "sha", DateTimeOffset.UtcNow)
            ]),
            new CommercialSafeEvaluator());

        TranslationRouteSelection route = await router.ResolveRouteAsync("en", "fr", commercialSafeMode: false, CancellationToken.None);

        Assert.True(route.IsAvailable);
        Assert.Equal(TranslationRoutingKind.Pivot, route.RoutingKind);
        Assert.Equal("madlad400", route.ProviderName);
        Assert.Equal("google/madlad400-3b-mt", route.ModelId);
    }

    [Fact]
    public async Task ResolveRouteAsync_WhenMadladHasOnlyQuantizedEncoder_SelectsPivotRoute()
    {
        using var workspace = new TranslationRouterTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateTranslationSpec(
                "Helsinki-NLP/opus-mt-en-es",
                [ "opus-en-es", "helsinki-opus-en-es" ],
                "manifest-models/opus-en-es"),
            CreateMadladSpec());
        string madladCacheRoot = workspace.CreateCacheRoot("google/madlad400-3b-mt");
        workspace.WriteMadladCacheFiles(madladCacheRoot, includeDefaultEncoder: false);

        var router = new TranslationLanguageRouter(
            registry,
            new InMemoryModelCacheInventory(
            [
                new LocalModelCacheRecord("google/madlad400-3b-mt", madladCacheRoot, "main", "sha", DateTimeOffset.UtcNow)
            ]),
            new CommercialSafeEvaluator());

        TranslationRouteSelection route = await router.ResolveRouteAsync("en", "fr", commercialSafeMode: false, CancellationToken.None);

        Assert.True(route.IsAvailable);
        Assert.Equal(TranslationRoutingKind.Pivot, route.RoutingKind);
        Assert.EndsWith("encoder_model_int8.onnx", route.ResolvedModelEntryPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSupportedTargetLanguagesAsync_WhenMadladIsMissing_ReportsUnavailablePairsClearly()
    {
        using var workspace = new TranslationRouterTestWorkspace();
        BundledModelManifestRegistry registry = workspace.WriteManifest(
            CreateTranslationSpec(
                "Helsinki-NLP/opus-mt-en-es",
                [ "opus-en-es", "helsinki-opus-en-es" ],
                "manifest-models/opus-en-es"));
        string opusCacheRoot = workspace.CreateCacheRoot("Helsinki-NLP/opus-mt-en-es");
        workspace.WriteOpusCacheFiles(opusCacheRoot);

        var router = new TranslationLanguageRouter(
            registry,
            new InMemoryModelCacheInventory(
            [
                new LocalModelCacheRecord("Helsinki-NLP/opus-mt-en-es", opusCacheRoot, "main", "sha", DateTimeOffset.UtcNow)
            ]),
            new CommercialSafeEvaluator());

        IReadOnlyList<TranslationTargetLanguageOption> options = await router.GetSupportedTargetLanguagesAsync(
            "en",
            commercialSafeMode: false,
            CancellationToken.None);

        TranslationTargetLanguageOption spanish = Assert.Single(options, option => option.LanguageCode == "es");
        TranslationTargetLanguageOption french = Assert.Single(options, option => option.LanguageCode == "fr");
        TranslationTargetLanguageOption japanese = Assert.Single(options, option => option.LanguageCode == "ja");

        Assert.True(spanish.IsAvailable);
        Assert.Equal(TranslationRoutingKind.Direct, spanish.RoutingKind);
        Assert.False(french.IsAvailable);
        Assert.Contains("MADLAD-400 pivot is unavailable", french.Detail, StringComparison.Ordinal);
        Assert.False(japanese.IsAvailable);
        Assert.Contains("MADLAD-400 pivot is unavailable", japanese.Detail, StringComparison.Ordinal);
    }

    private static ManifestSpec CreateTranslationSpec(
        string modelId,
        IReadOnlyList<string> aliases,
        string rootFolder) =>
        new(
            modelId,
            "translation",
            "Apache-2.0",
            CommercialAllowed: true,
            RequiresAttribution: true,
            Aliases: aliases,
            RootFolder: rootFolder,
            BenchmarkEntry: "encoder_model.onnx",
            Variants:
            [
                new ManifestVariantSpec("merged-decoder", "decoder_model_merged.onnx")
            ]);

    private static ManifestSpec CreateMadladSpec() =>
        new(
            "google/madlad400-3b-mt",
            "translation",
            "Apache-2.0",
            CommercialAllowed: true,
            RequiresAttribution: true,
            Aliases: [ "madlad400-mt", "madlad400" ],
            RootFolder: "manifest-models/madlad400",
            BenchmarkEntry: "encoder_model.onnx",
            Variants:
            [
                new ManifestVariantSpec("int8", "encoder_model_int8.onnx"),
                new ManifestVariantSpec("fp16", "encoder_model_fp16.onnx")
            ]);

    private sealed class TranslationRouterTestWorkspace : IDisposable
    {
        public TranslationRouterTestWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"babelstudio-translation-router-{Guid.NewGuid():N}");
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
                        requires_user_consent = false,
                        voice_cloning = false,
                        commercial_safe_mode = model.CommercialAllowed,
                        source_url = $"https://example.invalid/{model.ModelId.Replace('/', '-')}",
                        revision = "main",
                        sha256 = "",
                        aliases = model.Aliases,
                        root_path = $"./{model.RootFolder}",
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
            string cacheRoot = Path.Combine(RootPath, "machine-cache", name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(cacheRoot);
            return cacheRoot;
        }

        public void WriteOpusCacheFiles(string cacheRoot)
        {
            WriteCacheFile(cacheRoot, "encoder_model.onnx");
            WriteCacheFile(cacheRoot, "decoder_model_merged.onnx");
            WriteCacheFile(cacheRoot, "vocab.json", "{}");
            WriteCacheFile(cacheRoot, "source.model");
            WriteCacheFile(cacheRoot, "target.model");
        }

        public void WriteMadladCacheFiles(string cacheRoot, bool includeDefaultEncoder = true)
        {
            if (includeDefaultEncoder)
            {
                WriteCacheFile(cacheRoot, "encoder_model.onnx");
            }

            WriteCacheFile(cacheRoot, "encoder_model_int8.onnx");
            WriteCacheFile(cacheRoot, "decoder_model_int8.onnx");
            WriteCacheFile(cacheRoot, "spiece.model");
        }

        private static void WriteCacheFile(string cacheRoot, string relativePath, string contents = "placeholder")
        {
            string filePath = Path.Combine(cacheRoot, relativePath);
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, contents);
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
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
        IReadOnlyList<string> Aliases,
        string RootFolder,
        string BenchmarkEntry,
        IReadOnlyList<ManifestVariantSpec> Variants);

    private sealed record ManifestVariantSpec(
        string Alias,
        string EntryPath);
}
