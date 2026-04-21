using BabelStudio.Application.Projects;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteProjectDatabase
{
    private const string StageRunIdColumnName = "stage_run_id";
    private const string ProvenanceColumnName = "provenance";
    private const string RequestedProviderColumnName = "RequestedProvider";
    private const string SelectedProviderColumnName = "SelectedProvider";
    private const string RuntimeModelIdColumnName = "RuntimeModelId";
    private const string RuntimeModelAliasColumnName = "RuntimeModelAlias";
    private const string RuntimeModelVariantColumnName = "RuntimeModelVariant";
    private const string BootstrapDetailColumnName = "BootstrapDetail";
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

            CREATE TABLE IF NOT EXISTS StageRuns (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                StageName TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL,
                FailureReason TEXT NULL,
                RequestedProvider TEXT NULL,
                SelectedProvider TEXT NULL,
                RuntimeModelId TEXT NULL,
                RuntimeModelAlias TEXT NULL,
                RuntimeModelVariant TEXT NULL,
                BootstrapDetail TEXT NULL,
                FOREIGN KEY (ProjectId) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS transcript_revisions (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                stage_run_id TEXT NULL,
                revision_number INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (stage_run_id) REFERENCES StageRuns(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS transcript_segments (
                id TEXT NOT NULL PRIMARY KEY,
                transcript_revision_id TEXT NOT NULL,
                segment_index INTEGER NOT NULL,
                start_seconds REAL NOT NULL,
                end_seconds REAL NOT NULL,
                text TEXT NOT NULL,
                FOREIGN KEY (transcript_revision_id) REFERENCES transcript_revisions(id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_artifacts_project_relative_path
                ON artifacts (project_id, relative_path);
            CREATE INDEX IF NOT EXISTS ix_stage_runs_project_id
                ON StageRuns (ProjectId, StartedAtUtc);
            CREATE INDEX IF NOT EXISTS ix_transcript_revisions_project_id
                ON transcript_revisions (project_id, revision_number);
            CREATE INDEX IF NOT EXISTS ix_transcript_segments_revision_id
                ON transcript_segments (transcript_revision_id, segment_index);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureArtifactColumnAsync(connection, StageRunIdColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureArtifactColumnAsync(connection, ProvenanceColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureStageRunColumnAsync(connection, RequestedProviderColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureStageRunColumnAsync(connection, SelectedProviderColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureStageRunColumnAsync(connection, RuntimeModelIdColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureStageRunColumnAsync(connection, RuntimeModelAliasColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureStageRunColumnAsync(connection, RuntimeModelVariantColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureStageRunColumnAsync(connection, BootstrapDetailColumnName, "TEXT NULL", cancellationToken).ConfigureAwait(false);
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

    private static async Task EnsureArtifactColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await ArtifactColumnExistsAsync(connection, columnName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE artifacts ADD COLUMN {columnName} {columnDefinition};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ArtifactColumnExistsAsync(
        SqliteConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        return await TableColumnExistsAsync(connection, "artifacts", columnName, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureStageRunColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await StageRunColumnExistsAsync(connection, columnName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE StageRuns ADD COLUMN {columnName} {columnDefinition};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> StageRunColumnExistsAsync(
        SqliteConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        return await TableColumnExistsAsync(connection, "StageRuns", columnName, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TableColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
