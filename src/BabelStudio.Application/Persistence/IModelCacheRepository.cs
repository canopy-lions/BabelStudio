using System.Data.Common;
using BabelStudio.Domain;

namespace BabelStudio.Application.Persistence;

public interface IModelCacheRepository
{
    Task UpsertAsync(
        DbConnection connection,
        LocalModelCacheRecord record,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<LocalModelCacheRecord?> GetAsync(
        DbConnection connection,
        string modelId,
        CancellationToken cancellationToken = default);
}
