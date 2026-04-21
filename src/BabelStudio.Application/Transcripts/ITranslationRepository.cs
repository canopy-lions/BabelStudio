using BabelStudio.Domain.Translation;

namespace BabelStudio.Application.Transcripts;

public interface ITranslationRepository
{
    Task<TranslationRevision?> GetCurrentRevisionAsync(
        Guid projectId,
        string targetLanguage,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranslatedSegment>> GetSegmentsAsync(
        Guid translationRevisionId,
        CancellationToken cancellationToken);

    Task<int> GetNextRevisionNumberAsync(
        Guid projectId,
        string targetLanguage,
        CancellationToken cancellationToken);

    Task SaveRevisionAsync(
        TranslationRevision revision,
        IReadOnlyList<TranslatedSegment> segments,
        CancellationToken cancellationToken);
}
