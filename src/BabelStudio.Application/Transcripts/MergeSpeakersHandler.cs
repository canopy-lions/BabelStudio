namespace BabelStudio.Application.Transcripts;

internal sealed class MergeSpeakersHandler(
    ISpeakerRepository speakerRepository,
    ITranscriptRepository transcriptRepository)
{
    private readonly ISpeakerRepository speakerRepository = speakerRepository ?? throw new ArgumentNullException(nameof(speakerRepository));
    private readonly ITranscriptRepository transcriptRepository = transcriptRepository ?? throw new ArgumentNullException(nameof(transcriptRepository));

    public async Task HandleAsync(
        Guid projectId,
        Guid sourceSpeakerId,
        Guid targetSpeakerId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (sourceSpeakerId == Guid.Empty)
        {
            throw new ArgumentException("Source speaker id is required.", nameof(sourceSpeakerId));
        }

        if (targetSpeakerId == Guid.Empty)
        {
            throw new ArgumentException("Target speaker id is required.", nameof(targetSpeakerId));
        }

        if (sourceSpeakerId == targetSpeakerId)
        {
            throw new InvalidOperationException("Source and target speaker must be different.");
        }

        await transcriptRepository.ReassignSpeakerAsync(
            projectId,
            sourceSpeakerId,
            targetSpeakerId,
            cancellationToken).ConfigureAwait(false);

        await speakerRepository.MergeSpeakersAsync(
            projectId,
            sourceSpeakerId,
            targetSpeakerId,
            cancellationToken).ConfigureAwait(false);
    }
}
