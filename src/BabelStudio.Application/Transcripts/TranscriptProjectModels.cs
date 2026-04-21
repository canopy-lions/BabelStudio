using BabelStudio.Domain;
using BabelStudio.Domain.Transcript;
using BabelStudio.Application.Projects;
using BabelStudio.Domain.Translation;

namespace BabelStudio.Application.Transcripts;

public sealed record CreateTranscriptProjectRequest(
    string ProjectName,
    string SourceMediaPath);

public sealed record SaveTranscriptEditsRequest(
    Guid TranscriptRevisionId,
    IReadOnlyList<EditedTranscriptSegment> Segments);

public sealed record SetTranscriptLanguageRequest(
    string? TranscriptLanguage);

public sealed record GenerateTranslationRequest(
    string SourceLanguage,
    string TargetLanguage);

public sealed record SaveTranslationEditsRequest(
    Guid TranslationRevisionId,
    string TargetLanguage,
    IReadOnlyList<EditedTranslatedSegment> Segments);

public sealed record EditedTranscriptSegment(
    Guid SegmentId,
    string Text);

public sealed record EditedTranslatedSegment(
    int SegmentIndex,
    string Text);

public sealed record TranscriptProjectState(
    OpenProjectResult ProjectState,
    TranscriptRevision? CurrentTranscriptRevision,
    IReadOnlyList<TranscriptSegment> TranscriptSegments,
    TranslationRevision? CurrentTranslationRevision,
    IReadOnlyList<TranslatedSegment> TranslatedSegments,
    bool IsTranslationStale,
    string? TranscriptLanguage,
    IReadOnlyList<StageRunRecord> StageRuns);
