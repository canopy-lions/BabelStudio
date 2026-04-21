using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Composition.Runtime.Planning;

public sealed class BundledManifestModelCacheInventory : IModelCacheInventory
{
    private readonly BundledModelManifestRegistry manifestRegistry;

    public BundledManifestModelCacheInventory(BundledModelManifestRegistry manifestRegistry)
    {
        this.manifestRegistry = manifestRegistry ?? throw new ArgumentNullException(nameof(manifestRegistry));
    }

    public Task<IReadOnlyList<LocalModelCacheRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LocalModelCacheRecord> records = manifestRegistry.Entries
            .Where(entry => Directory.Exists(entry.RootDirectory))
            .Select(entry => new LocalModelCacheRecord(
                entry.ModelId,
                entry.RootDirectory,
                entry.Revision,
                entry.Sha256,
                DateTimeOffset.MinValue))
            .ToArray();

        return Task.FromResult(records);
    }
}
