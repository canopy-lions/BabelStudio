namespace BabelStudio.Application.Contracts;

public sealed record ArtifactWriteHandle(
    string RelativePath,
    string FinalPath,
    string TemporaryPath);
