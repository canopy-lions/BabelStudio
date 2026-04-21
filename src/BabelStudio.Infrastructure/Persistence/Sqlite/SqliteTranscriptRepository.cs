using BabelStudio.Application.Transcripts;
using BabelStudio.Domain.Transcript;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteTranscriptRepository : ITranscriptRepository
{
    private readonly SqliteProjectDatabase database;

    public SqliteTranscriptRepository(SqliteProjectDatabase database)
    {
        this.database = database;
    }

    public async Task<TranscriptRevision?> GetCurrentRevisionAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   project_id,
                   stage_run_id,
                   revision_number,
                   created_at_utc
            FROM transcript_revisions
            WHERE project_id = $projectId
            ORDER BY revision_number DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadRevision(reader);
    }

    public async Task<IReadOnlyList<TranscriptSegment>> GetSegmentsAsync(Guid transcriptRevisionId, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   transcript_revision_id,
                   segment_index,
                   start_seconds,
                   end_seconds,
                   text
            FROM transcript_segments
            WHERE transcript_revision_id = $transcriptRevisionId
            ORDER BY segment_index;
            """;
        command.Parameters.AddWithValue("$transcriptRevisionId", transcriptRevisionId.ToString("D"));

        var results = new List<TranscriptSegment>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TranscriptSegment(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5)));
        }

        return results;
    }

    public async Task<int> GetNextRevisionNumberAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(MAX(revision_number), 0)
            FROM transcript_revisions
            WHERE project_id = $projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        int currentValue = Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        return currentValue + 1;
    }

    public async Task SaveRevisionAsync(
        TranscriptRevision revision,
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(segments);

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand revisionCommand = connection.CreateCommand())
        {
            revisionCommand.Transaction = transaction;
            revisionCommand.CommandText =
                """
                INSERT INTO transcript_revisions (
                    id,
                    project_id,
                    stage_run_id,
                    revision_number,
                    created_at_utc)
                VALUES (
                    $id,
                    $projectId,
                    $stageRunId,
                    $revisionNumber,
                    $createdAtUtc);
                """;
            revisionCommand.Parameters.AddWithValue("$id", revision.Id.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$projectId", revision.ProjectId.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$stageRunId", revision.StageRunId?.ToString("D") ?? (object)DBNull.Value);
            revisionCommand.Parameters.AddWithValue("$revisionNumber", revision.RevisionNumber);
            revisionCommand.Parameters.AddWithValue("$createdAtUtc", revision.CreatedAtUtc.UtcDateTime);
            await revisionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (TranscriptSegment segment in segments.OrderBy(segment => segment.SegmentIndex))
        {
            await using SqliteCommand segmentCommand = connection.CreateCommand();
            segmentCommand.Transaction = transaction;
            segmentCommand.CommandText =
                """
                INSERT INTO transcript_segments (
                    id,
                    transcript_revision_id,
                    segment_index,
                    start_seconds,
                    end_seconds,
                    text)
                VALUES (
                    $id,
                    $transcriptRevisionId,
                    $segmentIndex,
                    $startSeconds,
                    $endSeconds,
                    $text);
                """;
            segmentCommand.Parameters.AddWithValue("$id", segment.Id.ToString("D"));
            segmentCommand.Parameters.AddWithValue("$transcriptRevisionId", segment.TranscriptRevisionId.ToString("D"));
            segmentCommand.Parameters.AddWithValue("$segmentIndex", segment.SegmentIndex);
            segmentCommand.Parameters.AddWithValue("$startSeconds", segment.StartSeconds);
            segmentCommand.Parameters.AddWithValue("$endSeconds", segment.EndSeconds);
            segmentCommand.Parameters.AddWithValue("$text", segment.Text);
            await segmentCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TranscriptRevision ReadRevision(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)));
}
