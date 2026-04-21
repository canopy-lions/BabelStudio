using BabelStudio.Domain;
using BabelStudio.Infrastructure.Persistence.Repositories;
using BabelStudio.Inference.Runtime.Planning;

namespace BabelStudio.Composition.Runtime.Planning;

public sealed class LocalModelCacheInventory : IModelCacheInventory
{
    private readonly LocalModelCacheRecordStore recordStore;

    public LocalModelCacheInventory(LocalModelCacheRecordStore recordStore)
    {
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
    }

    public async Task<IReadOnlyList<LocalModelCacheRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
        await recordStore.LoadAsync(cancellationToken).ConfigureAwait(false);
}
