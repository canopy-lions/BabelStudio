using System.Text.Json;
using BabelStudio.Domain;
using BabelStudio.Infrastructure.Settings;

namespace BabelStudio.Infrastructure.Persistence.Repositories;

public sealed class LocalModelCacheRecordStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly BabelStudioStoragePaths storagePaths;

    public LocalModelCacheRecordStore(BabelStudioStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths;
    }

    public async Task<IReadOnlyList<LocalModelCacheRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storagePaths.ModelCacheIndexPath))
        {
            return [];
        }

        await using FileStream stream = File.OpenRead(storagePaths.ModelCacheIndexPath);
        LocalModelCacheRecord[]? records = await JsonSerializer.DeserializeAsync<LocalModelCacheRecord[]>(
            stream,
            SerializerOptions,
            cancellationToken);

        return records ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyList<LocalModelCacheRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        Directory.CreateDirectory(storagePaths.ModelCacheDirectory);
        await using FileStream stream = File.Create(storagePaths.ModelCacheIndexPath);
        await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, cancellationToken);
    }
}
