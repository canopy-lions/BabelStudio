using BabelStudio.Application.Contracts;
using BabelStudio.Domain.Projects;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly SqliteProjectDatabase database;

    public SqliteProjectRepository(SqliteProjectDatabase database)
    {
        this.database = database;
    }

    public async Task InitializeAsync(BabelProject project, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO projects (id, name, created_at_utc, updated_at_utc)
            VALUES ($id, $name, $createdAtUtc, $updatedAtUtc);
            """;
        command.Parameters.AddWithValue("$id", project.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$createdAtUtc", project.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("$updatedAtUtc", project.UpdatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BabelProject?> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(database.DatabasePath))
        {
            return null;
        }

        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, created_at_utc, updated_at_utc
            FROM projects
            LIMIT 1;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new BabelProject(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)));
    }
}
