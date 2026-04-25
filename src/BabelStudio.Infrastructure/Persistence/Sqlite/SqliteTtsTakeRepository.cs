using BabelStudio.Application.Transcripts;
using BabelStudio.Domain.Tts;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteTtsTakeRepository : ITtsTakeRepository
{
    private readonly SqliteProjectDatabase database;

    public SqliteTtsTakeRepository(SqliteProjectDatabase database)
    {
        this.database = database;
    }

    public async Task<TtsTake?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadTake(reader)
            : null;
    }

    public async Task<IReadOnlyList<TtsTake>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE project_id = $projectId ORDER BY created_at_utc, segment_index;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TtsTake>> GetBySegmentAsync(
        Guid translatedSegmentId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE translated_segment_id = $translatedSegmentId ORDER BY created_at_utc;";
        command.Parameters.AddWithValue("$translatedSegmentId", translatedSegmentId.ToString("D"));
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TtsTake>> GetStaleBySpeakerAsync(
        Guid projectId,
        Guid voiceAssignmentId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            {SelectColumns}
            WHERE project_id = $projectId
              AND voice_assignment_id = $voiceAssignmentId
              AND is_stale = 1
            ORDER BY segment_index, created_at_utc;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$voiceAssignmentId", voiceAssignmentId.ToString("D"));
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkBySegmentIndicesStaleAsync(
        Guid projectId,
        IReadOnlySet<int> segmentIndices,
        CancellationToken cancellationToken)
    {
        if (segmentIndices.Count == 0)
        {
            return;
        }

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE tts_takes
            SET is_stale = 1,
                status = $status
            WHERE project_id = $projectId
              AND segment_index = $segmentIndex;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$status", TtsTakeStatus.Stale.ToString());
        SqliteParameter segmentIndexParameter = command.Parameters.Add("$segmentIndex", SqliteType.Integer);
        foreach (int segmentIndex in segmentIndices)
        {
            segmentIndexParameter.Value = segmentIndex;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkByVoiceAssignmentStaleAsync(
        Guid projectId,
        Guid voiceAssignmentId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE tts_takes
            SET is_stale = 1,
                status = $status
            WHERE project_id = $projectId
              AND voice_assignment_id = $voiceAssignmentId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$voiceAssignmentId", voiceAssignmentId.ToString("D"));
        command.Parameters.AddWithValue("$status", TtsTakeStatus.Stale.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(TtsTake take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(take);

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO tts_takes (
                id,
                project_id,
                voice_assignment_id,
                translated_segment_id,
                segment_index,
                translated_text_hash,
                artifact_id,
                stage_run_id,
                status,
                is_stale,
                duration_samples,
                sample_rate,
                provider,
                model_id,
                voice_id,
                duration_overrun_ratio,
                created_at_utc)
            VALUES (
                $id,
                $projectId,
                $voiceAssignmentId,
                $translatedSegmentId,
                $segmentIndex,
                $translatedTextHash,
                $artifactId,
                $stageRunId,
                $status,
                $isStale,
                $durationSamples,
                $sampleRate,
                $provider,
                $modelId,
                $voiceId,
                $durationOverrunRatio,
                $createdAtUtc)
            ON CONFLICT(id) DO UPDATE SET
                artifact_id = excluded.artifact_id,
                stage_run_id = excluded.stage_run_id,
                status = excluded.status,
                is_stale = excluded.is_stale,
                duration_samples = excluded.duration_samples,
                sample_rate = excluded.sample_rate,
                provider = excluded.provider,
                model_id = excluded.model_id,
                voice_id = excluded.voice_id,
                duration_overrun_ratio = excluded.duration_overrun_ratio;
            """;
        BindTake(command, take);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SelectColumns =
        """
        SELECT id,
               project_id,
               voice_assignment_id,
               translated_segment_id,
               segment_index,
               translated_text_hash,
               artifact_id,
               stage_run_id,
               status,
               is_stale,
               duration_samples,
               sample_rate,
               provider,
               model_id,
               voice_id,
               duration_overrun_ratio,
               created_at_utc
        FROM tts_takes
        """;

    private static async Task<IReadOnlyList<TtsTake>> ReadAllAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<TtsTake>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTake(reader));
        }

        return results;
    }

    private static void BindTake(SqliteCommand command, TtsTake take)
    {
        command.Parameters.AddWithValue("$id", take.Id.ToString("D"));
        command.Parameters.AddWithValue("$projectId", take.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$voiceAssignmentId", take.VoiceAssignmentId.ToString("D"));
        command.Parameters.AddWithValue("$translatedSegmentId", take.TranslatedSegmentId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$segmentIndex", take.SegmentIndex);
        command.Parameters.AddWithValue("$translatedTextHash", take.TranslatedTextHash ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$artifactId", take.ArtifactId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$stageRunId", take.StageRunId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", take.Status.ToString());
        command.Parameters.AddWithValue("$isStale", take.IsStale ? 1 : 0);
        command.Parameters.AddWithValue("$durationSamples", take.DurationSamples ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sampleRate", take.SampleRate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$provider", take.Provider ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$modelId", take.ModelId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$voiceId", take.VoiceId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$durationOverrunRatio", take.DurationOverrunRatio ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", take.CreatedAtUtc.UtcDateTime);
    }

    private static TtsTake ReadTake(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
            Enum.Parse<TtsTakeStatus>(reader.GetString(8), ignoreCase: true),
            reader.GetInt64(9) == 1,
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetDouble(15),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(16), DateTimeKind.Utc)));
}
