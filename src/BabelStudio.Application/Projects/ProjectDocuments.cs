using BabelStudio.Application.Contracts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;

namespace BabelStudio.Application.Projects;

public static class ProjectDocumentVersions
{
    public const int CurrentProjectSchemaVersion = 1;
}

public sealed record ProjectManifest(
    Guid ProjectId,
    string Name,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ProjectManifest FromProject(BabelProject project) =>
        new(
            project.Id,
            project.Name,
            SchemaVersion: ProjectDocumentVersions.CurrentProjectSchemaVersion,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
}

public sealed record SourceMediaReference(
    string OriginalPath,
    string OriginalFileName,
    FileFingerprint Fingerprint,
    MediaProbeSnapshot Probe,
    DateTimeOffset CapturedAtUtc);

public enum SourceMediaStatus
{
    Unknown = 0,
    Available = 1,
    Missing = 2,
    Changed = 3
}
