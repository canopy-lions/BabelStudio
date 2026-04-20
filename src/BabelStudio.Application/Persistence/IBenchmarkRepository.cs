using System.Data.Common;
using BabelStudio.Domain;

namespace BabelStudio.Application.Persistence;

public interface IBenchmarkRepository
{
    Task AddAsync(
        DbConnection connection,
        BenchmarkRunRecord run,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BenchmarkRunRecord>> ListByModelIdAsync(
        DbConnection connection,
        string modelId,
        CancellationToken cancellationToken = default);
}
