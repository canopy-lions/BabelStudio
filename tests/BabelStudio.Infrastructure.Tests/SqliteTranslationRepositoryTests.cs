using BabelStudio.Domain;
using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Transcript;
using BabelStudio.Domain.Translation;
using BabelStudio.Infrastructure.Persistence.Sqlite;

namespace BabelStudio.Infrastructure.Tests;

public sealed class SqliteTranslationRepositoryTests
{
    [Fact]
    public async Task Repository_round_trips_current_translation_revision_segments_and_revision_numbers_per_language()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "Translation.babelstudio");
        try
        {
            var database = new SqliteProjectDatabase(projectRoot);
            var projectRepository = new SqliteProjectRepository(database);
            var transcriptRepository = new SqliteTranscriptRepository(database);
            var translationRepository = new SqliteTranslationRepository(database);
            var stageRunStore = new SqliteProjectStageRunStore(database);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var project = new BabelProject(Guid.NewGuid(), "Translation", now, now);

            await projectRepository.InitializeAsync(project, CancellationToken.None);

            TranscriptRevision transcriptRevision = TranscriptRevision.Create(project.Id, stageRunId: null, revisionNumber: 1, now.AddSeconds(1));
            TranscriptSegment[] transcriptSegments =
            [
                TranscriptSegment.Create(transcriptRevision.Id, 0, 0.0, 1.5, "Hello"),
                TranscriptSegment.Create(transcriptRevision.Id, 1, 1.5, 3.0, "World")
            ];
            await transcriptRepository.SaveRevisionAsync(transcriptRevision, transcriptSegments, CancellationToken.None);

            StageRunRecord translationStageRun = StageRunRecord.Start(project.Id, "translation", now.AddSeconds(2))
                .WithRuntimeInfo("auto", "cpu", "Helsinki-NLP/opus-mt-en-es", "opus-en-es", "merged-decoder", "bootstrap skipped")
                .Complete(now.AddSeconds(3));
            await stageRunStore.CreateAsync(translationStageRun, CancellationToken.None);
            await stageRunStore.UpdateAsync(translationStageRun, CancellationToken.None);

            TranslationRevision translationRevision = TranslationRevision.Create(
                project.Id,
                translationStageRun.Id,
                transcriptRevision.Id,
                "es",
                revisionNumber: 1,
                now.AddSeconds(4),
                translationProvider: "opus-mt",
                modelId: "Helsinki-NLP/opus-mt-en-es",
                executionProvider: "cpu");
            TranslatedSegment[] translatedSegments =
            [
                TranslatedSegment.Create(translationRevision.Id, 0, 0.0, 1.5, "Hola", "hash-0"),
                TranslatedSegment.Create(translationRevision.Id, 1, 1.5, 3.0, "Mundo", "hash-1")
            ];

            await translationRepository.SaveRevisionAsync(translationRevision, translatedSegments, CancellationToken.None);

            TranslationRevision? current = await translationRepository.GetCurrentRevisionAsync(project.Id, "es", CancellationToken.None);
            IReadOnlyList<TranslatedSegment> reloadedSegments = await translationRepository.GetSegmentsAsync(translationRevision.Id, CancellationToken.None);
            int nextSpanishRevision = await translationRepository.GetNextRevisionNumberAsync(project.Id, "es", CancellationToken.None);
            int nextGermanRevision = await translationRepository.GetNextRevisionNumberAsync(project.Id, "de", CancellationToken.None);

            Assert.NotNull(current);
            Assert.Equal(transcriptRevision.Id, current!.SourceTranscriptRevisionId);
            Assert.Equal("opus-mt", current.TranslationProvider);
            Assert.Equal("Helsinki-NLP/opus-mt-en-es", current.ModelId);
            Assert.Equal("cpu", current.ExecutionProvider);
            Assert.Equal(2, reloadedSegments.Count);
            Assert.Equal("Hola", reloadedSegments[0].Text);
            Assert.Equal("hash-0", reloadedSegments[0].SourceSegmentHash);
            Assert.Equal(2, nextSpanishRevision);
            Assert.Equal(1, nextGermanRevision);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
