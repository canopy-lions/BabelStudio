using BabelStudio.Application.Projects;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteProjectDatabase
{
    private readonly string databasePath;

    public SqliteProjectDatabase(string projectRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        databasePath = Path.Combine(Path.GetFullPath(projectRootPath), ProjectArtifactPaths.DatabaseFileName);
    }

    public string DatabasePath => databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS projects (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS media_assets (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_file_name TEXT NOT NULL,
                fingerprint_sha256 TEXT NOT NULL,
                source_size_bytes INTEGER NOT NULL,
                source_last_write_time_utc TEXT NOT NULL,
                format_name TEXT NOT NULL,
                duration_seconds REAL NOT NULL,
                has_audio INTEGER NOT NULL,
                has_video INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS artifacts (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                media_asset_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                duration_seconds REAL NULL,
                sample_rate INTEGER NULL,
                channel_count INTEGER NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (media_asset_id) REFERENCES media_assets(id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_artifacts_project_relative_path
                ON artifacts (project_id, relative_path);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
