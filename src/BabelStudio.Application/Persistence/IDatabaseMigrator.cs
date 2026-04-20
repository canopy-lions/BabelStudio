using BabelStudio.Domain;

namespace BabelStudio.Application.Persistence;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SchemaVersionRecord>> GetAppliedVersionsAsync(CancellationToken cancellationToken = default);
}
