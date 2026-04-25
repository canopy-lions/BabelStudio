using BabelStudio.Application.Projects;

namespace BabelStudio.Application.Tests;

public sealed class ProjectArtifactPathsTests
{
    // ── GetTtsTakeRelativePath ────────────────────────────────────────────────

    [Fact]
    public void GetTtsTakeRelativePath_ValidInputs_ReturnsExpectedPath()
    {
        Guid speakerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid segmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, takeNumber: 1);

        Assert.Equal(
            "artifacts/tts/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222-take-0001.wav",
            path);
    }

    [Fact]
    public void GetTtsTakeRelativePath_EmptySpeakerId_Throws()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.Empty, Guid.NewGuid(), takeNumber: 1));

        Assert.Contains("speakerId", ex.ParamName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTtsTakeRelativePath_EmptySegmentId_Throws()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.Empty, takeNumber: 1));

        Assert.Contains("segmentId", ex.ParamName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTtsTakeRelativePath_ZeroTakeNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), takeNumber: 0));
    }

    [Fact]
    public void GetTtsTakeRelativePath_NegativeTakeNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), takeNumber: -5));
    }

    [Theory]
    [InlineData(1, "0001")]
    [InlineData(9, "0009")]
    [InlineData(10, "0010")]
    [InlineData(999, "0999")]
    [InlineData(9999, "9999")]
    public void GetTtsTakeRelativePath_FormatsTheTakeNumberWithLeadingZeros(int takeNumber, string expectedPad)
    {
        Guid speakerId = Guid.NewGuid();
        Guid segmentId = Guid.NewGuid();

        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, segmentId, takeNumber);

        Assert.EndsWith($"-take-{expectedPad}.wav", path, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTtsTakeRelativePath_PathStartsWithTtsDirectory()
    {
        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), takeNumber: 1);

        Assert.StartsWith("artifacts/tts/", path, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTtsTakeRelativePath_PathEndsWithWavExtension()
    {
        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), Guid.NewGuid(), takeNumber: 3);

        Assert.EndsWith(".wav", path, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTtsTakeRelativePath_ContainsSpeakerIdInPath()
    {
        Guid speakerId = Guid.NewGuid();

        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(speakerId, Guid.NewGuid(), takeNumber: 1);

        Assert.Contains(speakerId.ToString("D"), path, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTtsTakeRelativePath_ContainsSegmentIdInFilename()
    {
        Guid segmentId = Guid.NewGuid();

        string path = ProjectArtifactPaths.GetTtsTakeRelativePath(Guid.NewGuid(), segmentId, takeNumber: 1);

        string filename = Path.GetFileName(path);
        Assert.StartsWith(segmentId.ToString("D"), filename, StringComparison.Ordinal);
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void TtsDirectoryRelativePath_HasExpectedValue()
    {
        Assert.Equal("artifacts/tts", ProjectArtifactPaths.TtsDirectoryRelativePath);
    }

    [Fact]
    public void RequiredDirectories_IncludesTtsDirectory()
    {
        Assert.Contains("artifacts/tts", ProjectArtifactPaths.RequiredDirectories);
    }
}