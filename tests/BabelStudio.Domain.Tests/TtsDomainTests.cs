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

    // ── TtsTake additional coverage ───────────────────────────────────────────

    [Fact]
    public void TtsTake_Fail_SetsFailedStatus()
    {
        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        TtsTake failed = take.Fail();

        Assert.Equal(TtsTakeStatus.Failed, failed.Status);
    }

    [Fact]
    public void TtsTake_Fail_DoesNotAlterOtherFields()
    {
        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        TtsTake failed = take.Fail();

        Assert.Equal(take.Id, failed.Id);
        Assert.Equal(take.ProjectId, failed.ProjectId);
        Assert.Equal(take.VoiceAssignmentId, failed.VoiceAssignmentId);
        Assert.Equal(take.IsStale, failed.IsStale);
        Assert.Null(failed.ArtifactId);
    }

    [Fact]
    public void TtsTake_Create_PreservesTranslatedSegmentId()
    {
        Guid segmentId = Guid.NewGuid();

        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid(), translatedSegmentId: segmentId);

        Assert.Equal(segmentId, take.TranslatedSegmentId);
    }

    [Fact]
    public void TtsTake_Create_WithoutTranslatedSegmentId_IsNull()
    {
        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(take.TranslatedSegmentId);
    }

    [Fact]
    public void TtsTake_Create_GeneratesUniqueIds()
    {
        Guid projectId = Guid.NewGuid();
        Guid voiceAssignmentId = Guid.NewGuid();

        TtsTake take1 = TtsTake.Create(projectId, voiceAssignmentId);
        TtsTake take2 = TtsTake.Create(projectId, voiceAssignmentId);

        Assert.NotEqual(take1.Id, take2.Id);
    }

    [Fact]
    public void TtsTake_MarkStale_IsImmutable()
    {
        TtsTake original = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        _ = original.MarkStale();

        // Original must not be mutated
        Assert.False(original.IsStale);
        Assert.Equal(TtsTakeStatus.Pending, original.Status);
    }

    [Fact]
    public void TtsTake_Create_SetsCreatedAtUtcToNow()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;

        TtsTake take = TtsTake.Create(Guid.NewGuid(), Guid.NewGuid());

        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.True(take.CreatedAtUtc >= before);
        Assert.True(take.CreatedAtUtc <= after);
    }

    // ── VoiceAssignment additional coverage ───────────────────────────────────

    [Fact]
    public void VoiceAssignment_Create_NullVoiceVariant_RemainsNull()
    {
        VoiceAssignment assignment = VoiceAssignment.Create(
            Guid.NewGuid(), Guid.NewGuid(), "kokoro-v1.0", voiceVariant: null);

        Assert.Null(assignment.VoiceVariant);
    }

    [Fact]
    public void VoiceAssignment_Create_WhitespaceVoiceVariant_IsStoredAsNull()
    {
        VoiceAssignment assignment = VoiceAssignment.Create(
            Guid.NewGuid(), Guid.NewGuid(), "kokoro-v1.0", voiceVariant: "   ");

        Assert.Null(assignment.VoiceVariant);
    }

    [Fact]
    public void VoiceAssignment_Create_RequiresConsent_IsPreserved()
    {
        VoiceAssignment assignment = VoiceAssignment.Create(
            Guid.NewGuid(), Guid.NewGuid(), "kokoro-v1.0", requiresConsent: true);

        Assert.True(assignment.RequiresConsent);
    }

    [Fact]
    public void VoiceAssignment_Create_DefaultRequiresConsentIsFalse()
    {
        VoiceAssignment assignment = VoiceAssignment.Create(
            Guid.NewGuid(), Guid.NewGuid(), "kokoro-v1.0");

        Assert.False(assignment.RequiresConsent);
    }

    [Fact]
    public void VoiceAssignment_Create_GeneratesUniqueIds()
    {
        Guid projectId = Guid.NewGuid();
        Guid speakerId = Guid.NewGuid();

        VoiceAssignment a1 = VoiceAssignment.Create(projectId, speakerId, "kokoro-v1.0");
        VoiceAssignment a2 = VoiceAssignment.Create(projectId, speakerId, "kokoro-v1.0");

        Assert.NotEqual(a1.Id, a2.Id);
    }

    [Fact]
    public void VoiceAssignment_Create_RejectsEmptyVoiceModelId()
    {
        Assert.Throws<ArgumentException>(() =>
            VoiceAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), ""));
    }
}
