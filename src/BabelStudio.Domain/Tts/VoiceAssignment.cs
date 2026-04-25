namespace BabelStudio.Domain.Tts;

public sealed record VoiceAssignment(
    Guid Id,
    Guid ProjectId,
    Guid SpeakerId,
    string VoiceModelId,
    string? VoiceVariant,
    bool RequiresConsent,
    DateTimeOffset CreatedAtUtc)
{
    public static VoiceAssignment Create(
        Guid projectId,
        Guid speakerId,
        string voiceModelId,
        string? voiceVariant = null,
        bool requiresConsent = false)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (speakerId == Guid.Empty)
        {
            throw new ArgumentException("Speaker id is required.", nameof(speakerId));
        }

        if (string.IsNullOrWhiteSpace(voiceModelId))
        {
            throw new ArgumentException("Voice model id is required.", nameof(voiceModelId));
        }

        return new VoiceAssignment(
            Guid.NewGuid(),
            projectId,
            speakerId,
            voiceModelId.Trim(),
            string.IsNullOrWhiteSpace(voiceVariant) ? null : voiceVariant.Trim(),
            requiresConsent,
            DateTimeOffset.UtcNow);
    }
}
