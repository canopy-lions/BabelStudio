using BabelStudio.Application.Contracts;

namespace BabelStudio.TestDoubles;

public sealed class FakeStudioSettingsService : IStudioSettingsService
{
    private const int RecentProjectLimit = 10;

    public StudioSettings CurrentSettings { get; private set; } = StudioSettings.Default;

    public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentSettings);

    public Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken)
    {
        CurrentSettings = Normalize(settings);
        return Task.CompletedTask;
    }

    public Task<StudioSettings> TouchRecentProjectAsync(
        string projectPath,
        string projectName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        string normalizedPath = Path.GetFullPath(projectPath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RecentProjectEntry entry = new(projectName.Trim(), normalizedPath, now);
        RecentProjectEntry[] updatedRecentProjects =
            [entry, .. CurrentSettings.RecentProjects
                .Where(candidate => !string.Equals(candidate.ProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.LastOpenedAtUtc)
                .Take(RecentProjectLimit - 1)];

        CurrentSettings = Normalize(CurrentSettings with { RecentProjects = updatedRecentProjects });
        return Task.FromResult(CurrentSettings);
    }

    private static StudioSettings Normalize(StudioSettings settings)
    {
        RecentProjectEntry[] recentProjects = settings.RecentProjects
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProjectPath) && !string.IsNullOrWhiteSpace(entry.ProjectName))
            .Select(entry => new RecentProjectEntry(
                entry.ProjectName.Trim(),
                Path.GetFullPath(entry.ProjectPath),
                entry.LastOpenedAtUtc))
            .OrderByDescending(entry => entry.LastOpenedAtUtc)
            .DistinctBy(entry => entry.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Take(RecentProjectLimit)
            .ToArray();

        return settings with
        {
            DefaultSourceLanguage = NormalizeLanguageCode(settings.DefaultSourceLanguage) ?? StudioSettings.Default.DefaultSourceLanguage,
            DefaultTargetLanguage = NormalizeLanguageCode(settings.DefaultTargetLanguage) ?? StudioSettings.Default.DefaultTargetLanguage,
            ModelTierPreference = string.IsNullOrWhiteSpace(settings.ModelTierPreference)
                ? StudioSettings.Default.ModelTierPreference
                : settings.ModelTierPreference.Trim().ToLowerInvariant(),
            WindowLayout = settings.WindowLayout ?? StudioSettings.Default.WindowLayout,
            RecentProjects = recentProjects
        };
    }

    private static string? NormalizeLanguageCode(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? null
            : languageCode.Trim().ToLowerInvariant();
}
