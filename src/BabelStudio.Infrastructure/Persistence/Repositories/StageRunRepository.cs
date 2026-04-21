using System.Data.Common;
using BabelStudio.Application.Persistence;
using BabelStudio.Domain;
using BabelStudio.Infrastructure.Persistence.Sqlite;
using Dapper;

namespace BabelStudio.Infrastructure.Persistence.Repositories;

public sealed class StageRunRepository : IStageRunRepository
{
    public async Task CreateAsync(
        DbConnection connection,
        StageRunRecord stageRun,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO StageRuns (Id, ProjectId, StageName, Status, StartedAtUtc, CompletedAtUtc, FailureReason)
            VALUES (@Id, @ProjectId, @StageName, @Status, @StartedAtUtc, @CompletedAtUtc, @FailureReason);
            """,
            ToParameters(stageRun),
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<StageRunRecord?> GetAsync(
        DbConnection connection,
        Guid stageRunId,
        CancellationToken cancellationToken = default)
    {
        StageRunRow? row = await connection.QuerySingleOrDefaultAsync<StageRunRow>(new CommandDefinition(
            """
            SELECT Id, ProjectId, StageName, Status, StartedAtUtc, CompletedAtUtc, FailureReason
            FROM StageRuns
            WHERE Id = @Id;
            """,
            new { Id = SqliteValueConverters.ToDbValue(stageRunId) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<StageRunRecord>> ListByProjectAsync(
        DbConnection connection,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StageRunRow> rows = (await connection.QueryAsync<StageRunRow>(new CommandDefinition(
            """
            SELECT Id, ProjectId, StageName, Status, StartedAtUtc, CompletedAtUtc, FailureReason
            FROM StageRuns
            WHERE ProjectId = @ProjectId
            ORDER BY StartedAtUtc, Id;
            """,
            new { ProjectId = SqliteValueConverters.ToDbValue(projectId) },
            cancellationToken: cancellationToken))).AsList();

        return rows.Select(ToDomain).ToArray();
    }

    public async Task CompleteAsync(
        DbConnection connection,
        StageRunRecord stageRun,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE StageRuns
            SET Status = @Status,
                CompletedAtUtc = @CompletedAtUtc,
                FailureReason = @FailureReason
            WHERE Id = @Id;
            """,
            ToParameters(stageRun),
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static object ToParameters(StageRunRecord stageRun) => new
    {
        Id = SqliteValueConverters.ToDbValue(stageRun.Id),
        ProjectId = SqliteValueConverters.ToDbValue(stageRun.ProjectId),
        stageRun.StageName,
        Status = stageRun.Status.ToString(),
        StartedAtUtc = SqliteValueConverters.ToDbValue(stageRun.StartedAtUtc),
        CompletedAtUtc = stageRun.CompletedAtUtc is DateTimeOffset completedAtUtc ? SqliteValueConverters.ToDbValue(completedAtUtc) : null,
        stageRun.FailureReason
    };

    private static StageRunRecord ToDomain(StageRunRow row) =>
        new(
            SqliteValueConverters.ParseGuid(row.Id),
            SqliteValueConverters.ParseGuid(row.ProjectId),
            row.StageName,
            Enum.Parse<StageRunStatus>(row.Status, ignoreCase: false),
            SqliteValueConverters.ParseDateTimeOffset(row.StartedAtUtc),
            string.IsNullOrWhiteSpace(row.CompletedAtUtc) ? null : SqliteValueConverters.ParseDateTimeOffset(row.CompletedAtUtc),
            row.FailureReason);

    private sealed record StageRunRow(
        string Id,
        string ProjectId,
        string StageName,
        string Status,
        string StartedAtUtc,
        string? CompletedAtUtc,
        string? FailureReason);
}
