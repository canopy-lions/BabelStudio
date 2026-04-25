using BabelStudio.Domain.Projects;
using BabelStudio.Domain.Tts;
using BabelStudio.Infrastructure.Persistence.Sqlite;

namespace BabelStudio.Infrastructure.Tests;

public sealed class SqliteTtsRepositoryTests
{
    [Fact]
    public async Task Repositories_round_trip_voice_assignment_and_tts_take_stale_markers()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "Tts.babelstudio");
        try
        {
            var database = new SqliteProjectDatabase(projectRoot);
            var projectRepository = new SqliteProjectRepository(database);
            var speakerRepository = new SqliteSpeakerRepository(database);
            var voiceAssignmentRepository = new SqliteVoiceAssignmentRepository(database);
            var ttsTakeRepository = new SqliteTtsTakeRepository(database);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var project = new BabelProject(Guid.NewGuid(), "TTS", now, now);

            await projectRepository.InitializeAsync(project, CancellationToken.None);
            var speaker = await speakerRepository.EnsureDefaultSpeakerAsync(project.Id, CancellationToken.None);
            VoiceAssignment assignment = VoiceAssignment.Create(project.Id, speaker.Id, "kokoro-onnx", "af_heart");
            await voiceAssignmentRepository.SaveAsync(assignment, CancellationToken.None);

            TtsTake take = TtsTake.Create(project.Id, assignment.Id, translatedSegmentId: null, segmentIndex: 2, "hash")
                with
                {
                    Status = TtsTakeStatus.Completed,
                    DurationSamples = 240,
                    SampleRate = 24000,
                    Provider = "fake",
                    ModelId = "fake-model",
                    VoiceId = "af_heart",
                    DurationOverrunRatio = 0.2d
                };
            await ttsTakeRepository.SaveAsync(take, CancellationToken.None);

            VoiceAssignment? reloadedAssignment = await voiceAssignmentRepository.GetAsync(project.Id, speaker.Id, CancellationToken.None);
            IReadOnlyList<TtsTake> takes = await ttsTakeRepository.GetByProjectAsync(project.Id, CancellationToken.None);
            await ttsTakeRepository.MarkBySegmentIndicesStaleAsync(project.Id, new HashSet<int> { 2 }, CancellationToken.None);
            TtsTake? staleTake = await ttsTakeRepository.GetAsync(take.Id, CancellationToken.None);

            Assert.NotNull(reloadedAssignment);
            Assert.Equal("af_heart", reloadedAssignment!.VoiceVariant);
            TtsTake reloadedTake = Assert.Single(takes);
            Assert.Equal("fake-model", reloadedTake.ModelId);
            Assert.Equal("af_heart", reloadedTake.VoiceId);
            Assert.Equal(0.2d, reloadedTake.DurationOverrunRatio);
            Assert.NotNull(staleTake);
            Assert.True(staleTake!.IsStale);
            Assert.Equal(TtsTakeStatus.Stale, staleTake.Status);
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
