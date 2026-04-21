namespace BabelStudio.Domain.Projects;

public sealed record BabelProject(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
