using BabelStudio.Domain.Transcript;

namespace BabelStudio.Application.Transcripts;

public interface ITranscriptRepository
{
    Task<TranscriptRevision?> GetCurrentRevisionAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TranscriptSegment>> GetSegmentsAsync(Guid transcriptRevisionId, CancellationToken cancellationToken);

    Task<int> GetNextRevisionNumberAsync(Guid projectId, CancellationToken cancellationToken);

    Task SaveRevisionAsync(
        TranscriptRevision revision,
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken);

    Task ReassignSpeakerAsync(
        Guid projectId,
        Guid sourceSpeakerId,
        Guid targetSpeakerId,
        CancellationToken cancellationToken);

    Task ReassignAndMergeSpeakersAsync(
        Guid projectId,
        Guid sourceSpeakerId,
        Guid targetSpeakerId,
        CancellationToken cancellationToken);
}
