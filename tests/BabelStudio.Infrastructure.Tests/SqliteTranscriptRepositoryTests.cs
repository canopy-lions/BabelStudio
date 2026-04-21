using BabelStudio.Domain;
using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Transcript;
using BabelStudio.Infrastructure.Persistence.Sqlite;

namespace BabelStudio.Infrastructure.Tests;

public sealed class SqliteTranscriptRepositoryTests
{
    [Fact]
    public async Task Repository_round_trips_current_revision_segments_and_stage_runs()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "Transcript.babelstudio");
        try
        {
            var database = new SqliteProjectDatabase(projectRoot);
            var projectRepository = new SqliteProjectRepository(database);
            var transcriptRepository = new SqliteTranscriptRepository(database);
            var stageRunStore = new SqliteProjectStageRunStore(database);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var project = new BabelProject(Guid.NewGuid(), "Transcript", now, now);

            await projectRepository.InitializeAsync(project, CancellationToken.None);

            StageRunRecord asrStageRun = StageRunRecord.Start(project.Id, "asr", now)
                .WithRuntimeInfo("auto", "cpu", "onnx-community/whisper-tiny", "whisper-tiny-onnx", "int8", "bootstrap skipped")
                .Complete(now.AddSeconds(2));
            await stageRunStore.CreateAsync(asrStageRun, CancellationToken.None);
            await stageRunStore.UpdateAsync(asrStageRun, CancellationToken.None);

            TranscriptRevision revision = TranscriptRevision.Create(project.Id, asrStageRun.Id, revisionNumber: 1, now.AddSeconds(3));
            TranscriptSegment[] segments =
            [
                TranscriptSegment.Create(revision.Id, 0, 0.0, 1.5, "Hello"),
                TranscriptSegment.Create(revision.Id, 1, 1.5, 3.0, "World")
            ];

            await transcriptRepository.SaveRevisionAsync(revision, segments, CancellationToken.None);

            TranscriptRevision? current = await transcriptRepository.GetCurrentRevisionAsync(project.Id, CancellationToken.None);
            IReadOnlyList<TranscriptSegment> reloadedSegments = await transcriptRepository.GetSegmentsAsync(revision.Id, CancellationToken.None);
            IReadOnlyList<StageRunRecord> stageRuns = await stageRunStore.ListByProjectAsync(project.Id, CancellationToken.None);

            Assert.NotNull(current);
            Assert.Equal(asrStageRun.Id, current!.StageRunId);
            Assert.Equal(2, reloadedSegments.Count);
            Assert.Equal("Hello", reloadedSegments[0].Text);
            Assert.Single(stageRuns);
            Assert.Equal(StageRunStatus.Completed, stageRuns[0].Status);
            Assert.NotNull(stageRuns[0].RuntimeInfo);
            Assert.Equal("cpu", stageRuns[0].RuntimeInfo!.SelectedProvider);
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
