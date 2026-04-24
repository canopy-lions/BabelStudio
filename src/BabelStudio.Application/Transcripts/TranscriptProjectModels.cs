using BabelStudio.Domain;
using BabelStudio.Domain.Speakers;
using BabelStudio.Domain.Transcript;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain.Translation;

namespace BabelStudio.Application.Transcripts;

public sealed record CreateTranscriptProjectRequest(
    string ProjectName,
    string SourceMediaPath,
    bool EnableSpeakerDiarization = true,
    bool CommercialSafeMode = false);

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

public sealed record SetTranslationTargetRequest(
    string? TargetLanguage);

public sealed record EditedTranscriptSegment(
    Guid SegmentId,
    string Text,
    Guid? SpeakerId = null);

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

public sealed record RenameSpeakerRequest(
    Guid SpeakerId,
    string DisplayName);

public sealed record MergeSpeakersRequest(
    Guid SourceSpeakerId,
    Guid TargetSpeakerId);

public sealed record AssignSpeakerToSegmentRequest(
    Guid TranscriptRevisionId,
    Guid SegmentId,
    Guid SpeakerId);

public sealed record SplitSpeakerTurnRequest(
    Guid SpeakerTurnId,
    double SplitSeconds);

public sealed record ExtractReferenceClipRequest(
    Guid SpeakerId,
    Guid? SpeakerTurnId = null);

public sealed record TranscriptProjectState(
    OpenProjectResult ProjectState,
    TranscriptRevision? CurrentTranscriptRevision,
    IReadOnlyList<TranscriptSegment> TranscriptSegments,
    IReadOnlyList<ProjectSpeaker> Speakers,
    IReadOnlyList<SpeakerTurn> SpeakerTurns,
    TranslationRevision? CurrentTranslationRevision,
    IReadOnlyList<TranslatedSegment> TranslatedSegments,
    bool IsTranslationStale,
    string? TranscriptLanguage,
    IReadOnlyList<StageRunRecord> StageRuns,
    IReadOnlyList<TranslationTargetLanguageOption> SupportedTargetLanguages,
    string? SelectedTranslationTargetLanguage,
    IReadOnlySet<int> StaleTranslatedSegmentIndices,
    WaveformSummary? WaveformSummary);
