using BabelStudio.Application.Projects;

namespace BabelStudio.Application.Tests;

public sealed class ProjectArtifactPathsTests
{
    [Fact]
    public void TtsDirectoryRelativePath_HasExpectedValue()
    {
        Assert.Equal("artifacts/tts", ProjectArtifactPaths.TtsDirectoryRelativePath);
    }

    [Fact]
    public void RequiredDirectories_ContainsTtsDirectory()
    {
        Assert.Contains("artifacts/tts", ProjectArtifactPaths.RequiredDirectories);
    }

    [Fact]
    public void GetTtsTakeRelativePath_ReturnsExpectedFormat()
    {
        Guid speakerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid segmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, takeNumber: 1);

        Assert.Equal(
            "artifacts/tts/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222-take-0001.wav",
            path);
    }

    [Fact]
    public void GetTtsTakeRelativePath_PadsTakeNumberToFourDigits()
    {
        Guid speakerId = Guid.NewGuid();
        Guid segmentId = Guid.NewGuid();

        string take1 = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, takeNumber: 1);
        string take99 = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, takeNumber: 99);
        string take1000 = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, takeNumber: 1000);

        Assert.EndsWith("-take-0001.wav", take1);
        Assert.EndsWith("-take-0099.wav", take99);
        Assert.EndsWith("-take-1000.wav", take1000);
    }

    [Fact]
    public void GetTtsTakeRelativePath_UsesDashSeparatedGuids()
    {
        Guid speakerId = Guid.NewGuid();
        Guid segmentId = Guid.NewGuid();

        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, 1);

        // Should use "D" format (lowercase hex with hyphens, no braces)
        Assert.Contains(speakerId.ToString("D"), path);
        Assert.Contains(segmentId.ToString("D"), path);
    }

    [Fact]
    public void GetTtsTakeRelativePath_StartsWithTtsDirectory()
    {
        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), 1);

        Assert.StartsWith("artifacts/tts/", path);
    }

    [Fact]
    public void GetTtsTakeRelativePath_EndsWithWavExtension()
    {
        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), 1);

        Assert.EndsWith(".wav", path);
    }

    [Fact]
    public void GetTtsTakeRelativePath_RejectsEmptySpeakerId()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.Empty, Guid.NewGuid(), 1));

        Assert.Contains("speakerId", ex.ParamName);
    }

    [Fact]
    public void GetTtsTakeRelativePath_RejectsEmptySegmentId()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.Empty, 1));

        Assert.Contains("segmentId", ex.ParamName);
    }

    [Fact]
    public void GetTtsTakeRelativePath_RejectsZeroTakeNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void GetTtsTakeRelativePath_RejectsNegativeTakeNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), -1));
    }

    [Fact]
    public void GetTtsTakeRelativePath_AcceptsMinimumValidTakeNumber()
    {
        // take number 1 is the minimum positive value
        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), 1);

        Assert.NotNull(path);
        Assert.EndsWith("-take-0001.wav", path);
    }

    [Theory]
    [InlineData(1, "0001")]
    [InlineData(9, "0009")]
    [InlineData(10, "0010")]
    [InlineData(100, "0100")]
    [InlineData(9999, "9999")]
    [InlineData(10000, "10000")]
    public void GetTtsTakeRelativePath_TakeNumberFormatting(int takeNumber, string expectedSuffix)
    {
        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), takeNumber);

        Assert.EndsWith($"-take-{expectedSuffix}.wav", path);
    }
}