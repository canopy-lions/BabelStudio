using BabelStudio.Application.Contracts;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteMediaAssetRepository : IMediaAssetRepository
{
    private readonly SqliteProjectDatabase database;

    public SqliteMediaAssetRepository(SqliteProjectDatabase database)
    {
        this.database = database;
    }

    public async Task SaveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO media_assets (
                id,
                project_id,
                source_file_name,
                fingerprint_sha256,
                source_size_bytes,
                source_last_write_time_utc,
                format_name,
                duration_seconds,
                has_audio,
                has_video,
                created_at_utc)
            VALUES (
                $id,
                $projectId,
                $sourceFileName,
                $fingerprintSha256,
                $sourceSizeBytes,
                $sourceLastWriteTimeUtc,
                $formatName,
                $durationSeconds,
                $hasAudio,
                $hasVideo,
                $createdAtUtc);
            """;
        BindMediaAsset(command, asset);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaAsset?> GetPrimaryAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!File.Exists(database.DatabasePath))
        {
            return null;
        }

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   project_id,
                   source_file_name,
                   fingerprint_sha256,
                   source_size_bytes,
                   source_last_write_time_utc,
                   format_name,
                   duration_seconds,
                   has_audio,
                   has_video,
                   created_at_utc
            FROM media_assets
            WHERE project_id = $projectId
            ORDER BY created_at_utc
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadMediaAsset(reader);
    }

    public async Task SaveArtifactAsync(ProjectArtifact artifact, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO artifacts (
                id,
                project_id,
                media_asset_id,
                stage_run_id,
                kind,
                relative_path,
                sha256,
                size_bytes,
                duration_seconds,
                sample_rate,
                channel_count,
                provenance,
                created_at_utc)
            VALUES (
                $id,
                $projectId,
                $mediaAssetId,
                $stageRunId,
                $kind,
                $relativePath,
                $sha256,
                $sizeBytes,
                $durationSeconds,
                $sampleRate,
                $channelCount,
                $provenance,
                $createdAtUtc);
            """;
        BindArtifact(command, artifact);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectArtifact>> GetArtifactsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!File.Exists(database.DatabasePath))
        {
            return [];
        }

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   project_id,
                   media_asset_id,
                   stage_run_id,
                   kind,
                   relative_path,
                   sha256,
                   size_bytes,
                   duration_seconds,
                   sample_rate,
                   channel_count,
                   provenance,
                   created_at_utc
            FROM artifacts
            WHERE project_id = $projectId
            ORDER BY created_at_utc, relative_path;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));

        var results = new List<ProjectArtifact>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadArtifact(reader));
        }

        return results;
    }

    private static void BindMediaAsset(SqliteCommand command, MediaAsset asset)
    {
        command.Parameters.AddWithValue("$id", asset.Id.ToString("D"));
        command.Parameters.AddWithValue("$projectId", asset.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$sourceFileName", asset.SourceFileName);
        command.Parameters.AddWithValue("$fingerprintSha256", asset.FingerprintSha256);
        command.Parameters.AddWithValue("$sourceSizeBytes", asset.SourceSizeBytes);
        command.Parameters.AddWithValue("$sourceLastWriteTimeUtc", asset.SourceLastWriteTimeUtc.UtcDateTime);
        command.Parameters.AddWithValue("$formatName", asset.FormatName);
        command.Parameters.AddWithValue("$durationSeconds", asset.DurationSeconds);
        command.Parameters.AddWithValue("$hasAudio", asset.HasAudio ? 1 : 0);
        command.Parameters.AddWithValue("$hasVideo", asset.HasVideo ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", asset.CreatedAtUtc.UtcDateTime);
    }

    private static MediaAsset ReadMediaAsset(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)),
            reader.GetString(6),
            reader.GetDouble(7),
            reader.GetInt64(8) == 1,
            reader.GetInt64(9) == 1,
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc)));

    private static void BindArtifact(SqliteCommand command, ProjectArtifact artifact)
    {
        command.Parameters.AddWithValue("$id", artifact.Id.ToString("D"));
        command.Parameters.AddWithValue("$projectId", artifact.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$mediaAssetId", artifact.MediaAssetId.ToString("D"));
        command.Parameters.AddWithValue("$stageRunId", artifact.StageRunId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$kind", artifact.Kind.ToString());
        command.Parameters.AddWithValue("$relativePath", artifact.RelativePath);
        command.Parameters.AddWithValue("$sha256", artifact.Sha256);
        command.Parameters.AddWithValue("$sizeBytes", artifact.SizeBytes);
        command.Parameters.AddWithValue("$durationSeconds", (object?)artifact.DurationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$sampleRate", (object?)artifact.SampleRate ?? DBNull.Value);
        command.Parameters.AddWithValue("$channelCount", (object?)artifact.ChannelCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$provenance", (object?)artifact.Provenance ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", artifact.CreatedAtUtc.UtcDateTime);
    }

    private static ProjectArtifact ReadArtifact(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Enum.Parse<ArtifactKind>(reader.GetString(4), ignoreCase: true),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(12), DateTimeKind.Utc)),
            reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            reader.IsDBNull(11) ? null : reader.GetString(11));
}
