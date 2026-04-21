using BabelStudio.Application.Transcripts;
using BabelStudio.Domain.Translation;
using Microsoft.Data.Sqlite;

namespace BabelStudio.Infrastructure.Persistence.Sqlite;

public sealed class SqliteTranslationRepository : ITranslationRepository
{
    private readonly SqliteProjectDatabase database;

    public SqliteTranslationRepository(SqliteProjectDatabase database)
    {
        this.database = database;
    }

    public async Task<TranslationRevision?> GetCurrentRevisionAsync(
        Guid projectId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   project_id,
                   stage_run_id,
                   source_transcript_revision_id,
                   target_language,
                   revision_number,
                   created_at_utc
            FROM translation_revisions
            WHERE project_id = $projectId
              AND target_language = $targetLanguage
            ORDER BY revision_number DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$targetLanguage", NormalizeLanguageCode(targetLanguage));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadRevision(reader);
    }

    public async Task<IReadOnlyList<TranslatedSegment>> GetSegmentsAsync(
        Guid translationRevisionId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   translation_revision_id,
                   segment_index,
                   start_seconds,
                   end_seconds,
                   text
            FROM translated_segments
            WHERE translation_revision_id = $translationRevisionId
            ORDER BY segment_index;
            """;
        command.Parameters.AddWithValue("$translationRevisionId", translationRevisionId.ToString("D"));

        var results = new List<TranslatedSegment>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TranslatedSegment(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5)));
        }

        return results;
    }

    public async Task<int> GetNextRevisionNumberAsync(
        Guid projectId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(MAX(revision_number), 0)
            FROM translation_revisions
            WHERE project_id = $projectId
              AND target_language = $targetLanguage;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$targetLanguage", NormalizeLanguageCode(targetLanguage));

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        int currentValue = Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        return currentValue + 1;
    }

    public async Task SaveRevisionAsync(
        TranslationRevision revision,
        IReadOnlyList<TranslatedSegment> segments,
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
                INSERT INTO translation_revisions (
                    id,
                    project_id,
                    stage_run_id,
                    source_transcript_revision_id,
                    target_language,
                    revision_number,
                    created_at_utc)
                VALUES (
                    $id,
                    $projectId,
                    $stageRunId,
                    $sourceTranscriptRevisionId,
                    $targetLanguage,
                    $revisionNumber,
                    $createdAtUtc);
                """;
            revisionCommand.Parameters.AddWithValue("$id", revision.Id.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$projectId", revision.ProjectId.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$stageRunId", revision.StageRunId?.ToString("D") ?? (object)DBNull.Value);
            revisionCommand.Parameters.AddWithValue("$sourceTranscriptRevisionId", revision.SourceTranscriptRevisionId.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$targetLanguage", revision.TargetLanguage);
            revisionCommand.Parameters.AddWithValue("$revisionNumber", revision.RevisionNumber);
            revisionCommand.Parameters.AddWithValue("$createdAtUtc", revision.CreatedAtUtc.UtcDateTime);
            await revisionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using SqliteCommand segmentCommand = connection.CreateCommand();
        segmentCommand.Transaction = transaction;
        segmentCommand.CommandText =
            """
            INSERT INTO translated_segments (
                id,
                translation_revision_id,
                segment_index,
                start_seconds,
                end_seconds,
                text)
            VALUES (
                $id,
                $translationRevisionId,
                $segmentIndex,
                $startSeconds,
                $endSeconds,
                $text);
            """;
        SqliteParameter segmentIdParameter = segmentCommand.Parameters.Add("$id", SqliteType.Text);
        SqliteParameter translationRevisionIdParameter = segmentCommand.Parameters.Add("$translationRevisionId", SqliteType.Text);
        SqliteParameter segmentIndexParameter = segmentCommand.Parameters.Add("$segmentIndex", SqliteType.Integer);
        SqliteParameter startSecondsParameter = segmentCommand.Parameters.Add("$startSeconds", SqliteType.Real);
        SqliteParameter endSecondsParameter = segmentCommand.Parameters.Add("$endSeconds", SqliteType.Real);
        SqliteParameter textParameter = segmentCommand.Parameters.Add("$text", SqliteType.Text);

        foreach (TranslatedSegment segment in segments.OrderBy(segment => segment.SegmentIndex))
        {
            segmentIdParameter.Value = segment.Id.ToString("D");
            translationRevisionIdParameter.Value = segment.TranslationRevisionId.ToString("D");
            segmentIndexParameter.Value = segment.SegmentIndex;
            startSecondsParameter.Value = segment.StartSeconds;
            endSecondsParameter.Value = segment.EndSeconds;
            textParameter.Value = segment.Text;
            await segmentCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TranslationRevision ReadRevision(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));

    private static string NormalizeLanguageCode(string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            throw new ArgumentException("Target language is required.", nameof(targetLanguage));
        }

        return targetLanguage.Trim().ToLowerInvariant();
    }
}
