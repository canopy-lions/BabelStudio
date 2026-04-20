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

        await using var stream = new FileStream(
            storagePaths.ModelCacheIndexPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        LocalModelCacheRecord[]? records = await JsonSerializer.DeserializeAsync<LocalModelCacheRecord[]>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return records ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyList<LocalModelCacheRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        Directory.CreateDirectory(storagePaths.ModelCacheDirectory);
        await using var stream = new FileStream(
            storagePaths.ModelCacheIndexPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
