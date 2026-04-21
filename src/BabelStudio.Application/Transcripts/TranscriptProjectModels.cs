using BabelStudio.Domain;
using BabelStudio.Domain.Transcript;
using BabelStudio.Application.Projects;

namespace BabelStudio.Application.Transcripts;

public sealed record CreateTranscriptProjectRequest(
    string ProjectName,
    string SourceMediaPath);

public sealed record SaveTranscriptEditsRequest(
    Guid TranscriptRevisionId,
    IReadOnlyList<EditedTranscriptSegment> Segments);

public sealed record EditedTranscriptSegment(
    Guid SegmentId,
    string Text);

public sealed record TranscriptProjectState(
    OpenProjectResult ProjectState,
    TranscriptRevision? CurrentTranscriptRevision,
    IReadOnlyList<TranscriptSegment> TranscriptSegments,
    IReadOnlyList<StageRunRecord> StageRuns);
