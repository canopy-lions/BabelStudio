using BabelStudio.Application.Transcripts;
using BabelStudio.Domain;
using BabelStudio.Infrastructure.Persistence.Repositories;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteProjectStageRunStore : IProjectStageRunStore
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteProjectDatabase database;
    private readonly StageRunRepository repository = new();

    public SqliteProjectStageRunStore(SqliteProjectDatabase database)
    {
        this.database = database;
        connectionFactory = new SqliteConnectionFactory(database.DatabasePath);
    }

    public async Task CreateAsync(StageRunRecord stageRun, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await repository.CreateAsync(connection, stageRun, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(StageRunRecord stageRun, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await repository.CompleteAsync(connection, stageRun, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StageRunRecord>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await repository.ListByProjectAsync(connection, projectId, cancellationToken).ConfigureAwait(false);
    }
}
