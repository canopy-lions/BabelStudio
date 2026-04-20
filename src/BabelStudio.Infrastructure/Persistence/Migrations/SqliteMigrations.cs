namespace BabelStudio.Infrastructure.Persistence.Migrations;

internal static class SqliteMigrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new(
            1,
            "create-project-spine",
            """
            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                RootPath TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MediaAssets (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                OriginalPath TEXT NOT NULL,
                ContentHash TEXT NOT NULL,
                Kind TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS StageRuns (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                StageName TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL,
                FailureReason TEXT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Artifacts (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                StageRunId TEXT NULL,
                Kind TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                ContentHash TEXT NOT NULL,
                Provenance TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (StageRunId) REFERENCES StageRuns(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS ModelCache (
                ModelId TEXT PRIMARY KEY,
                RootPath TEXT NOT NULL,
                Revision TEXT NOT NULL,
                Sha256 TEXT NOT NULL,
                CachedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS BenchmarkRuns (
                Id TEXT PRIMARY KEY,
                ModelId TEXT NOT NULL,
                ModelPath TEXT NOT NULL,
                ReportPath TEXT NOT NULL,
                Status TEXT NOT NULL,
                RequestedProvider TEXT NOT NULL,
                SelectedProvider TEXT NOT NULL,
                RunCount INTEGER NOT NULL,
                SupportsExecution INTEGER NOT NULL,
                ModelSizeBytes INTEGER NOT NULL,
                ColdLoadMilliseconds REAL NULL,
                WarmLatencyAverageMilliseconds REAL NULL,
                WarmLatencyMinimumMilliseconds REAL NULL,
                WarmLatencyMaximumMilliseconds REAL NULL,
                FailureReason TEXT NULL,
                GeneratedAtUtc TEXT NOT NULL
            );
            """),
        new(
            2,
            "create-transcript-and-export-spine",
            """
            CREATE TABLE IF NOT EXISTS Speakers (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS SpeakerTurns (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                SpeakerId TEXT NOT NULL,
                StageRunId TEXT NULL,
                StartSeconds REAL NOT NULL,
                EndSeconds REAL NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (SpeakerId) REFERENCES Speakers(Id) ON DELETE CASCADE,
                FOREIGN KEY (StageRunId) REFERENCES StageRuns(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS TranscriptRevisions (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                StageRunId TEXT NULL,
                RevisionNumber INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (StageRunId) REFERENCES StageRuns(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS TranscriptSegments (
                Id TEXT PRIMARY KEY,
                TranscriptRevisionId TEXT NOT NULL,
                SpeakerId TEXT NULL,
                SegmentIndex INTEGER NOT NULL,
                StartSeconds REAL NOT NULL,
                EndSeconds REAL NOT NULL,
                Text TEXT NOT NULL,
                FOREIGN KEY (TranscriptRevisionId) REFERENCES TranscriptRevisions(Id) ON DELETE CASCADE,
                FOREIGN KEY (SpeakerId) REFERENCES Speakers(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS Words (
                Id TEXT PRIMARY KEY,
                TranscriptSegmentId TEXT NOT NULL,
                WordIndex INTEGER NOT NULL,
                StartSeconds REAL NOT NULL,
                EndSeconds REAL NOT NULL,
                Text TEXT NOT NULL,
                FOREIGN KEY (TranscriptSegmentId) REFERENCES TranscriptSegments(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS TranslationRevisions (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                StageRunId TEXT NULL,
                SourceTranscriptRevisionId TEXT NULL,
                TargetLanguage TEXT NOT NULL,
                RevisionNumber INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (StageRunId) REFERENCES StageRuns(Id) ON DELETE SET NULL,
                FOREIGN KEY (SourceTranscriptRevisionId) REFERENCES TranscriptRevisions(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS TranslatedSegments (
                Id TEXT PRIMARY KEY,
                TranslationRevisionId TEXT NOT NULL,
                SourceSegmentId TEXT NULL,
                SegmentIndex INTEGER NOT NULL,
                Text TEXT NOT NULL,
                FOREIGN KEY (TranslationRevisionId) REFERENCES TranslationRevisions(Id) ON DELETE CASCADE,
                FOREIGN KEY (SourceSegmentId) REFERENCES TranscriptSegments(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS VoiceAssignments (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                SpeakerId TEXT NOT NULL,
                VoiceModelId TEXT NOT NULL,
                VoiceVariant TEXT NULL,
                RequiresConsent INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (SpeakerId) REFERENCES Speakers(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS TtsTakes (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                VoiceAssignmentId TEXT NOT NULL,
                ArtifactId TEXT NULL,
                StageRunId TEXT NULL,
                Status TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (VoiceAssignmentId) REFERENCES VoiceAssignments(Id) ON DELETE CASCADE,
                FOREIGN KEY (ArtifactId) REFERENCES Artifacts(Id) ON DELETE SET NULL,
                FOREIGN KEY (StageRunId) REFERENCES StageRuns(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS MixPlans (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                StageRunId TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (StageRunId) REFERENCES StageRuns(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS Exports (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                MixPlanId TEXT NULL,
                ArtifactId TEXT NULL,
                ExportKind TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (MixPlanId) REFERENCES MixPlans(Id) ON DELETE SET NULL,
                FOREIGN KEY (ArtifactId) REFERENCES Artifacts(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS ConsentRecords (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                SubjectId TEXT NOT NULL,
                ConsentKind TEXT NOT NULL,
                GrantedAtUtc TEXT NOT NULL,
                Notes TEXT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );
            """),
        new(
            3,
            "create-core-indexes",
            """
            CREATE INDEX IF NOT EXISTS IX_Artifacts_ProjectId ON Artifacts(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_Artifacts_StageRunId ON Artifacts(StageRunId);
            CREATE INDEX IF NOT EXISTS IX_StageRuns_ProjectId ON StageRuns(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_MediaAssets_ProjectId ON MediaAssets(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_BenchmarkRuns_ModelId ON BenchmarkRuns(ModelId);
            CREATE INDEX IF NOT EXISTS IX_TranscriptRevisions_ProjectId ON TranscriptRevisions(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_TranslationRevisions_ProjectId ON TranslationRevisions(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_ConsentRecords_ProjectId ON ConsentRecords(ProjectId);
            """)
    ];
}
