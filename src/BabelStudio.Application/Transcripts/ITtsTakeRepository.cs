using BabelStudio.Domain.Tts;

namespace BabelStudio.Application.Transcripts;

public interface ITtsTakeRepository
{
    Task<TtsTake?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TtsTake>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TtsTake>> GetBySegmentAsync(Guid translatedSegmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TtsTake>> GetStaleBySpeakerAsync(Guid projectId, Guid voiceAssignmentId, CancellationToken cancellationToken);
    Task MarkBySegmentIndicesStaleAsync(Guid projectId, IReadOnlySet<int> segmentIndices, CancellationToken cancellationToken);
    Task MarkByVoiceAssignmentStaleAsync(Guid projectId, Guid voiceAssignmentId, CancellationToken cancellationToken);
    Task SaveAsync(TtsTake take, CancellationToken cancellationToken);
}
