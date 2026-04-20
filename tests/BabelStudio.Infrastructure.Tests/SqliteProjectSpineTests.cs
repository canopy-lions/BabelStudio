using System.Data.Common;
using BabelStudio.Domain;
using BabelStudio.Infrastructure.Persistence.Migrations;
using BabelStudio.Infrastructure.Persistence.Repositories;
using BabelStudio.Infrastructure.Persistence.Sqlite;

namespace BabelStudio.Infrastructure.Tests;

public sealed class SqliteProjectSpineTests
{
    [Fact]
    public async Task MigrationRunner_CreatesDatabaseFromScratch()
    {
        await using var database = new TemporarySqliteDatabase();

        await database.Migrator.MigrateAsync();

        Assert.True(File.Exists(database.DatabasePath));

        await using DbConnection connection = await database.ConnectionFactory.CreateOpenConnectionAsync();
        IReadOnlyList<string> tables = await ReadTableNamesAsync(connection);

        Assert.Contains("Projects", tables);
        Assert.Contains("StageRuns", tables);
        Assert.Contains("Artifacts", tables);
        Assert.Contains("ModelCache", tables);
        Assert.Contains("BenchmarkRuns", tables);
        Assert.Contains("ConsentRecords", tables);
    }

    [Fact]
    public async Task MigrationRunner_AppliesVersionsInOrder()
    {
        await using var database = new TemporarySqliteDatabase();

        await database.Migrator.MigrateAsync();
        await database.Migrator.MigrateAsync();

        IReadOnlyList<SchemaVersionRecord> versions = await database.Migrator.GetAppliedVersionsAsync();

        Assert.Equal([1, 2, 3], versions.Select(version => version.Version).ToArray());
    }

    [Fact]
    public async Task ProjectRepository_CanCreateAndReopenProject()
    {
        await using var database = new TemporarySqliteDatabase();
        await database.Migrator.MigrateAsync();

        ProjectRecord project = ProjectRecord.CreateNew("Demo", Path.Combine(Path.GetTempPath(), "babelstudio-demo"), DateTimeOffset.UtcNow);
        var repository = new ProjectRepository();

        await using (DbConnection connection = await database.ConnectionFactory.CreateOpenConnectionAsync())
        {
            await repository.CreateAsync(connection, project);
        }

        await using DbConnection reopenedConnection = await database.ConnectionFactory.CreateOpenConnectionAsync();
        ProjectRecord? reopened = await repository.GetAsync(reopenedConnection, project.Id);

        Assert.NotNull(reopened);
        Assert.Equal(project.Name, reopened!.Name);
        Assert.Equal(project.RootPath, reopened.RootPath);
    }

    [Fact]
    public async Task StageRunRepository_CanCreateAndCompleteStageRun()
    {
        await using var database = new TemporarySqliteDatabase();
        await database.Migrator.MigrateAsync();

        ProjectRecord project = ProjectRecord.CreateNew("Demo", Path.Combine(Path.GetTempPath(), "babelstudio-stage"), DateTimeOffset.UtcNow);
        StageRunRecord stageRun = StageRunRecord.Start(project.Id, "asr", DateTimeOffset.UtcNow);
        StageRunRecord completed = stageRun.Complete(DateTimeOffset.UtcNow.AddMinutes(1));

        var projectRepository = new ProjectRepository();
        var stageRunRepository = new StageRunRepository();

        await using DbConnection connection = await database.ConnectionFactory.CreateOpenConnectionAsync();
        await projectRepository.CreateAsync(connection, project);
        await stageRunRepository.CreateAsync(connection, stageRun);
        await stageRunRepository.CompleteAsync(connection, completed);

        StageRunRecord? reloaded = await stageRunRepository.GetAsync(connection, stageRun.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(StageRunStatus.Completed, reloaded!.Status);
        Assert.NotNull(reloaded.CompletedAtUtc);
    }

    [Fact]
    public async Task ArtifactRepository_CanRegisterArtifactWithProvenance()
    {
        await using var database = new TemporarySqliteDatabase();
        await database.Migrator.MigrateAsync();

        ProjectRecord project = ProjectRecord.CreateNew("Demo", Path.Combine(Path.GetTempPath(), "babelstudio-artifact"), DateTimeOffset.UtcNow);
        StageRunRecord stageRun = StageRunRecord.Start(project.Id, "translation", DateTimeOffset.UtcNow);
        ArtifactRecord artifact = ArtifactRecord.Register(
            project.Id,
            stageRun.Id,
            "transcript",
            Path.Combine("artifacts", "transcript.json"),
            "abc123",
            "translation-stage",
            DateTimeOffset.UtcNow);

        var projectRepository = new ProjectRepository();
        var stageRunRepository = new StageRunRepository();
        var artifactRepository = new ArtifactRepository();

        await using DbConnection connection = await database.ConnectionFactory.CreateOpenConnectionAsync();
        await projectRepository.CreateAsync(connection, project);
        await stageRunRepository.CreateAsync(connection, stageRun);
        await artifactRepository.RegisterAsync(connection, artifact);

        IReadOnlyList<ArtifactRecord> artifacts = await artifactRepository.ListByProjectAsync(connection, project.Id);

        ArtifactRecord stored = Assert.Single(artifacts);
        Assert.Equal("abc123", stored.ContentHash);
        Assert.Equal("translation-stage", stored.Provenance);
        Assert.Equal(Path.Combine("artifacts", "transcript.json"), stored.RelativePath);
    }

    [Fact]
    public async Task Repositories_RollbackTransactionLeavesNoRows()
    {
        await using var database = new TemporarySqliteDatabase();
        await database.Migrator.MigrateAsync();

        ProjectRecord project = ProjectRecord.CreateNew("Demo", Path.Combine(Path.GetTempPath(), "babelstudio-rollback"), DateTimeOffset.UtcNow);
        ArtifactRecord artifact = ArtifactRecord.Register(
            project.Id,
            null,
            "artifact",
            Path.Combine("artifacts", "rollback.json"),
            "deadbeef",
            "rollback-test",
            DateTimeOffset.UtcNow);

        var projectRepository = new ProjectRepository();
        var artifactRepository = new ArtifactRepository();

        await using (DbConnection connection = await database.ConnectionFactory.CreateOpenConnectionAsync())
        await using (DbTransaction transaction = await connection.BeginTransactionAsync())
        {
            await projectRepository.CreateAsync(connection, project, transaction);
            await artifactRepository.RegisterAsync(connection, artifact, transaction);
            await transaction.RollbackAsync();
        }

        await using DbConnection verificationConnection = await database.ConnectionFactory.CreateOpenConnectionAsync();
        ProjectRecord? reloadedProject = await projectRepository.GetAsync(verificationConnection, project.Id);
        IReadOnlyList<ArtifactRecord> artifacts = await artifactRepository.ListByProjectAsync(verificationConnection, project.Id);

        Assert.Null(reloadedProject);
        Assert.Empty(artifacts);
    }

    [Fact]
    public async Task ModelCacheRepository_CanUpsertRecord()
    {
        await using var database = new TemporarySqliteDatabase();
        await database.Migrator.MigrateAsync();

        LocalModelCacheRecord record = new("onnx-community/silero-vad", @"D:\models\silero-vad", "main", "abc123", DateTimeOffset.UtcNow);
        var repository = new ModelCacheRepository();

        await using DbConnection connection = await database.ConnectionFactory.CreateOpenConnectionAsync();
        await repository.UpsertAsync(connection, record);

        LocalModelCacheRecord? reloaded = await repository.GetAsync(connection, record.ModelId);

        Assert.NotNull(reloaded);
        Assert.Equal(record.RootPath, reloaded!.RootPath);
        Assert.Equal(record.Sha256, reloaded.Sha256);
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(DbConnection connection)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        public TemporarySqliteDatabase()
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), $"babelstudio-{Guid.NewGuid():N}.db");
            ConnectionFactory = new SqliteConnectionFactory(DatabasePath);
            Migrator = new SqliteDatabaseMigrator(ConnectionFactory);
        }

        public string DatabasePath { get; }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public SqliteDatabaseMigrator Migrator { get; }

        public ValueTask DisposeAsync()
        {
            DeleteIfPresent(DatabasePath);
            DeleteIfPresent($"{DatabasePath}-wal");
            DeleteIfPresent($"{DatabasePath}-shm");
            return ValueTask.CompletedTask;
        }

        private static void DeleteIfPresent(string path)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
