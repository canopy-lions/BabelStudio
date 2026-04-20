using BabelStudio.Domain;

namespace BabelStudio.Domain.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void CreateNewProject_NormalizesNameAndPath()
    {
        ProjectRecord project = ProjectRecord.CreateNew(" Demo Project ", @".\workspace", DateTimeOffset.Parse("2026-04-19T12:00:00+00:00"));

        Assert.Equal("Demo Project", project.Name);
        Assert.True(Path.IsPathRooted(project.RootPath));
        Assert.Equal(project.CreatedAtUtc, project.UpdatedAtUtc);
    }

    [Fact]
    public void RegisterArtifact_RejectsRootedPath()
    {
        Assert.Throws<ArgumentException>(() => ArtifactRecord.Register(
            Guid.NewGuid(),
            null,
            "transcript",
            @"D:\absolute\artifact.json",
            "abc123",
            "asr-stage",
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CompleteStageRun_RejectsEarlierCompletionTime()
    {
        StageRunRecord stageRun = StageRunRecord.Start(Guid.NewGuid(), "asr", DateTimeOffset.Parse("2026-04-19T12:00:00+00:00"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            stageRun.Complete(DateTimeOffset.Parse("2026-04-19T11:59:59+00:00")));
    }
}
