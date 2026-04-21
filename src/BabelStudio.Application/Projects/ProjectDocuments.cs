using BabelStudio.Application.Contracts;
using BabelStudio.Domain.Media;
using BabelStudio.Domain.Projects;

namespace BabelStudio.Application.Projects;

public static class ProjectDocumentVersions
{
    public const int CurrentProjectSchemaVersion = 2;
}

public sealed record ProjectManifest(
    Guid ProjectId,
    string Name,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? TranscriptLanguage = null)
{
    public static ProjectManifest FromProject(BabelProject project, string? transcriptLanguage = null) =>
        new(
            project.Id,
            project.Name,
            SchemaVersion: ProjectDocumentVersions.CurrentProjectSchemaVersion,
            project.CreatedAtUtc,
            project.UpdatedAtUtc,
            NormalizeLanguageCode(transcriptLanguage));

    public ProjectManifest WithTranscriptLanguage(string? transcriptLanguage) =>
        this with
        {
            SchemaVersion = ProjectDocumentVersions.CurrentProjectSchemaVersion,
            TranscriptLanguage = NormalizeLanguageCode(transcriptLanguage)
        };

    private static string? NormalizeLanguageCode(string? transcriptLanguage) =>
        string.IsNullOrWhiteSpace(transcriptLanguage)
            ? null
            : transcriptLanguage.Trim().ToLowerInvariant();
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
