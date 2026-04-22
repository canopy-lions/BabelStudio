using BabelStudio.Domain;
using BabelStudio.Domain.Transcript;
using BabelStudio.Application.Contracts;
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
    string TargetLanguage,
    bool CommercialSafeMode);

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

public sealed record RelocateTranscriptSourceRequest(
    string NewSourceMediaPath);

public sealed record SplitTranscriptSegmentRequest(
    Guid TranscriptRevisionId,
    Guid SegmentId,
    double SplitSeconds);

public sealed record MergeTranscriptSegmentsRequest(
    Guid TranscriptRevisionId,
    Guid FirstSegmentId,
    Guid SecondSegmentId);

public sealed record TrimTranscriptSegmentRequest(
    Guid TranscriptRevisionId,
    Guid SegmentId,
    double StartSeconds,
    double EndSeconds);

public sealed record DeleteTranscriptSegmentRequest(
    Guid TranscriptRevisionId,
    Guid SegmentId);

public sealed record TranscriptProjectState(
    OpenProjectResult ProjectState,
    TranscriptRevision? CurrentTranscriptRevision,
    IReadOnlyList<TranscriptSegment> TranscriptSegments,
    TranslationRevision? CurrentTranslationRevision,
    IReadOnlyList<TranslatedSegment> TranslatedSegments,
    bool IsTranslationStale,
    string? TranscriptLanguage,
    IReadOnlyList<StageRunRecord> StageRuns,
    WaveformSummary? WaveformSummary);
