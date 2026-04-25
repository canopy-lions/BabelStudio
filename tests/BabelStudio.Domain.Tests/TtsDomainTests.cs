using BabelStudio.Domain.Tts;

namespace BabelStudio.Domain.Tests;

public sealed class TtsDomainTests
{
    [Fact]
    public void TtsTake_Create_SetsDefaultStatus()
    {
        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(TtsTakeStatus.Pending, take.Status);
        Assert.False(take.IsStale);
        Assert.Null(take.ArtifactId);
        Assert.Null(take.DurationSamples);
        Assert.Null(take.SampleRate);
    }

    [Fact]
    public void TtsTake_MarkStale_SetsStaleStatus()
    {
        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        TtsTake stale = take.MarkStale();

        Assert.True(stale.IsStale);
        Assert.Equal(TtsTakeStatus.Stale, stale.Status);
    }

    [Fact]
    public void TtsTake_Complete_SetsCompletedAndClearsStale()
    {
        Guid artifactId = Guid.NewGuid();
        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid()).MarkStale();

        TtsTake completed = take.Complete(artifactId, durationSamples: 24000, sampleRate: 24000, provider: "cpu");

        Assert.Equal(TtsTakeStatus.Completed, completed.Status);
        Assert.False(completed.IsStale);
        Assert.Equal(artifactId, completed.ArtifactId);
        Assert.Equal(24000, completed.DurationSamples);
        Assert.Equal(24000, completed.SampleRate);
        Assert.Equal("cpu", completed.Provider);
    }

    [Fact]
    public void TtsTake_Create_RejectsEmptyProjectId()
    {
        Assert.Throws<ArgumentException>(() => TtsTake.Create(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void TtsTake_Create_RejectsEmptyVoiceAssignmentId()
    {
        Assert.Throws<ArgumentException>(() => TtsTake.Create(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void VoiceAssignment_Create_RejectsEmptyProjectId()
    {
        Assert.Throws<ArgumentException>(() =>
            VoiceAssignment.Create(Guid.Empty, Guid.NewGuid(), "kokoro-v1.0"));
    }

    [Fact]
    public void VoiceAssignment_Create_RejectsEmptySpeakerId()
    {
        Assert.Throws<ArgumentException>(() =>
            VoiceAssignment.Create(Guid.NewGuid(), Guid.Empty, "kokoro-v1.0"));
    }

    [Fact]
    public void VoiceAssignment_Create_RejectsBlankVoiceModelId()
    {
        Assert.Throws<ArgumentException>(() =>
            VoiceAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "   "));
    }

    [Fact]
    public void VoiceAssignment_Create_NormalizesVoiceModelId()
    {
        VoiceAssignment assignment = VoiceAssignment.Create(
            Guid.NewGuid(), Guid.NewGuid(), " kokoro-v1.0 ", voiceVariant: " af_heart ");

        Assert.Equal("kokoro-v1.0", assignment.VoiceModelId);
        Assert.Equal("af_heart", assignment.VoiceVariant);
    }
}
