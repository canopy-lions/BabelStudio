using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;

namespace BabelStudio.Application.Projects;

public sealed record CreateProjectFromMediaRequest(
    string ProjectName,
    string SourceMediaPath);

public sealed record CreateProjectFromMediaResult(
    BabelProject Project,
    MediaAsset MediaAsset,
    SourceMediaReference SourceReference,
    ProjectArtifact AudioArtifact,
    ProjectArtifact WaveformArtifact);

public sealed record RelocateSourceMediaRequest(
    string NewSourceMediaPath);

public sealed record OpenProjectResult(
    BabelProject Project,
    MediaAsset? MediaAsset,
    SourceMediaReference? SourceReference,
    SourceMediaStatus SourceStatus,
    string? SourceStatusMessage,
    IReadOnlyList<ProjectArtifact> Artifacts,
    string? TranscriptLanguage);
