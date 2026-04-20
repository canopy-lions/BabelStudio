namespace BabelStudio.Inference.Runtime.ModelManifest;

public sealed class BundledModelManifestRegistry
{
    private readonly IReadOnlyDictionary<string, BundledModelManifestEntry> aliasIndex;

    private BundledModelManifestRegistry(
        string manifestPath,
        IReadOnlyList<BundledModelManifestEntry> entries,
        IReadOnlyDictionary<string, BundledModelManifestEntry> aliasIndex)
    {
        ManifestPath = manifestPath;
        Entries = entries;
        this.aliasIndex = aliasIndex;
    }

    public string ManifestPath { get; }

    public IReadOnlyList<BundledModelManifestEntry> Entries { get; }

    public static bool TryLoadDefault(out BundledModelManifestRegistry? registry, out string? error)
    {
        string? manifestPath = LocateDefaultManifestPath();
        if (manifestPath is null)
        {
            registry = null;
            error = "Bundled model manifest was not found.";
            return false;
        }

        try
        {
            registry = Load(manifestPath);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ModelManifestValidationException or InvalidOperationException)
        {
            registry = null;
            error = ex.Message;
            return false;
        }
    }

    public static BundledModelManifestRegistry Load(string manifestPath)
    {
        string fullManifestPath = Path.GetFullPath(manifestPath);
        ModelManifestCatalog catalog = ModelManifestLoader.LoadCatalog(fullManifestPath);

        var entries = catalog.Models
            .Select(model => NormalizeEntry(model, fullManifestPath))
            .ToArray();

        var aliasIndex = new Dictionary<string, BundledModelManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (BundledModelManifestEntry entry in entries)
        {
            if (entry.Aliases.Count == 0)
            {
                throw new InvalidOperationException($"Model '{entry.ModelId}' in '{fullManifestPath}' did not define any aliases.");
            }

            foreach (string alias in entry.Aliases)
            {
                if (!aliasIndex.TryAdd(alias, entry))
                {
                    throw new InvalidOperationException($"Alias '{alias}' is defined more than once in '{fullManifestPath}'.");
                }
            }
        }

        return new BundledModelManifestRegistry(fullManifestPath, entries, aliasIndex);
    }

    public bool TryResolve(string reference, out BundledModelManifestResolution? resolution)
    {
        resolution = null;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        string trimmedReference = reference.Trim();
        string alias = trimmedReference;
        string? variantAlias = null;

        int variantSeparatorIndex = trimmedReference.IndexOf('@');
        if (variantSeparatorIndex >= 0)
        {
            alias = trimmedReference[..variantSeparatorIndex];
            variantAlias = trimmedReference[(variantSeparatorIndex + 1)..];
        }

        if (!aliasIndex.TryGetValue(alias, out BundledModelManifestEntry? entry))
        {
            return false;
        }

        string resolvedEntryPath = entry.DefaultBenchmarkEntryPath;
        string resolvedVariantAlias = "default";
        if (!string.IsNullOrWhiteSpace(variantAlias))
        {
            BundledModelManifestVariant? variant = entry.Variants
                .FirstOrDefault(candidate => candidate.Alias.Equals(variantAlias, StringComparison.OrdinalIgnoreCase));

            if (variant is null)
            {
                throw new FileNotFoundException(
                    $"Model alias '{alias}' does not define benchmark variant '{variantAlias}'.",
                    trimmedReference);
            }

            resolvedEntryPath = variant.EntryPath;
            resolvedVariantAlias = variant.Alias;
        }

        resolution = new BundledModelManifestResolution(
            entry,
            trimmedReference,
            alias,
            resolvedVariantAlias,
            resolvedEntryPath);
        return true;
    }

    private static BundledModelManifestEntry NormalizeEntry(ModelManifest model, string manifestPath)
    {
        string manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException($"Manifest path '{manifestPath}' did not have a parent directory.");

        if (string.IsNullOrWhiteSpace(model.RootPath))
        {
            throw new InvalidOperationException($"Model '{model.ModelId}' in '{manifestPath}' did not define root_path.");
        }

        if (string.IsNullOrWhiteSpace(model.BenchmarkEntry))
        {
            throw new InvalidOperationException($"Model '{model.ModelId}' in '{manifestPath}' did not define benchmark_entry.");
        }

        string rootDirectory = Path.GetFullPath(Path.Combine(manifestDirectory, model.RootPath));
        string defaultBenchmarkEntryPath = Path.GetFullPath(Path.Combine(rootDirectory, model.BenchmarkEntry));
        BundledModelManifestVariant[] variants = model.Variants
            .Select(variant => new BundledModelManifestVariant(
                variant.Alias,
                Path.GetFullPath(Path.Combine(rootDirectory, variant.EntryPath))))
            .ToArray();

        return new BundledModelManifestEntry(
            model.ModelId,
            model.Task.ToManifestValue(),
            model.License.ToManifestValue(),
            model.CommercialAllowed,
            model.RedistributionAllowed,
            model.RequiresAttribution,
            model.RequiresUserConsent,
            model.VoiceCloning,
            model.CommercialSafeMode,
            model.SourceUrl,
            model.Revision,
            model.Sha256,
            model.Aliases,
            rootDirectory,
            defaultBenchmarkEntryPath,
            variants);
    }

    private static string? LocateDefaultManifestPath()
    {
        foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            foreach (string ancestor in EnumerateAncestors(seed))
            {
                string candidate = Path.Combine(
                    ancestor,
                    "src",
                    "BabelStudio.Inference",
                    "Runtime",
                    "ModelManifest",
                    "bundled-models.manifest.json");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAncestors(string path)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}

public sealed record BundledModelManifestEntry(
    string ModelId,
    string Task,
    string License,
    bool CommercialAllowed,
    bool RedistributionAllowed,
    bool RequiresAttribution,
    bool RequiresUserConsent,
    bool VoiceCloning,
    bool CommercialSafeMode,
    string SourceUrl,
    string Revision,
    string Sha256,
    IReadOnlyList<string> Aliases,
    string RootDirectory,
    string DefaultBenchmarkEntryPath,
    IReadOnlyList<BundledModelManifestVariant> Variants);

public sealed record BundledModelManifestVariant(
    string Alias,
    string EntryPath);

public sealed record BundledModelManifestResolution(
    BundledModelManifestEntry Entry,
    string RequestedReference,
    string Alias,
    string VariantAlias,
    string EntryPath);
