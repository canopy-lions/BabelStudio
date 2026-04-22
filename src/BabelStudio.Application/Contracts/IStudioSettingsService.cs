namespace BabelStudio.Application.Contracts;

public interface IStudioSettingsService
{
    Task<StudioSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken);

    Task<StudioSettings> TouchRecentProjectAsync(
        string projectPath,
        string projectName,
        CancellationToken cancellationToken);
}

public sealed record StudioSettings(
    string? DefaultSourceLanguage,
    string? DefaultTargetLanguage,
    string ModelTierPreference,
    bool CommercialSafeMode,
    WindowLayoutSettings WindowLayout,
    IReadOnlyList<RecentProjectEntry> RecentProjects)
{
    public static StudioSettings Default { get; } = new(
        DefaultSourceLanguage: "en",
        DefaultTargetLanguage: "es",
        ModelTierPreference: "balanced",
        CommercialSafeMode: true,
        WindowLayout: new WindowLayoutSettings(null, null, IsMaximized: false),
        RecentProjects: []);
}

public sealed record WindowLayoutSettings(
    double? Width,
    double? Height,
    bool IsMaximized);

public sealed record RecentProjectEntry(
    string ProjectName,
    string ProjectPath,
    DateTimeOffset LastOpenedAtUtc);
