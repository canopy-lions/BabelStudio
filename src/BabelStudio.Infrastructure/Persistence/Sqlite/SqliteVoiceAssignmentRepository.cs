using BabelStudio.Application.Transcripts;
using BabelStudio.Domain.Tts;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteVoiceAssignmentRepository : IVoiceAssignmentRepository
{
    private readonly SqliteProjectDatabase database;

    public SqliteVoiceAssignmentRepository(SqliteProjectDatabase database)
    {
        this.database = database;
    }

    public async Task<VoiceAssignment?> GetAsync(
        Guid projectId,
        Guid speakerId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   project_id,
                   speaker_id,
                   voice_model_id,
                   voice_variant,
                   requires_consent,
                   created_at_utc
            FROM voice_assignments
            WHERE project_id = $projectId
              AND speaker_id = $speakerId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$speakerId", speakerId.ToString("D"));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadAssignment(reader)
            : null;
    }

    public async Task<IReadOnlyList<VoiceAssignment>> GetAllAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   project_id,
                   speaker_id,
                   voice_model_id,
                   voice_variant,
                   requires_consent,
                   created_at_utc
            FROM voice_assignments
            WHERE project_id = $projectId
            ORDER BY created_at_utc;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));

        var results = new List<VoiceAssignment>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadAssignment(reader));
        }

        return results;
    }

    public async Task SaveAsync(VoiceAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO voice_assignments (
                id,
                project_id,
                speaker_id,
                voice_model_id,
                voice_variant,
                requires_consent,
                created_at_utc)
            VALUES (
                $id,
                $projectId,
                $speakerId,
                $voiceModelId,
                $voiceVariant,
                $requiresConsent,
                $createdAtUtc)
            ON CONFLICT(project_id, speaker_id) DO UPDATE SET
                voice_model_id = excluded.voice_model_id,
                voice_variant = excluded.voice_variant,
                requires_consent = excluded.requires_consent;
            """;
        command.Parameters.AddWithValue("$id", assignment.Id.ToString("D"));
        command.Parameters.AddWithValue("$projectId", assignment.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$speakerId", assignment.SpeakerId.ToString("D"));
        command.Parameters.AddWithValue("$voiceModelId", assignment.VoiceModelId);
        command.Parameters.AddWithValue("$voiceVariant", assignment.VoiceVariant ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$requiresConsent", assignment.RequiresConsent ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", assignment.CreatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM voice_assignments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static VoiceAssignment ReadAssignment(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt64(5) == 1,
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));
}
