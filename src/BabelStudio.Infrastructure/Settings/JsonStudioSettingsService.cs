using System.Text.Json;
using BabelStudio.Application.Contracts;

namespace BabelStudio.Infrastructure.Settings;

public sealed class JsonStudioSettingsService : IStudioSettingsService
{
    private const int RecentProjectLimit = 10;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly BabelStudioStoragePaths storagePaths;

    public JsonStudioSettingsService(BabelStudioStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths;
    }

    public async Task<StudioSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(storagePaths.SettingsPath))
        {
            return StudioSettings.Default;
        }

        try
        {
            await using FileStream stream = File.OpenRead(storagePaths.SettingsPath);
            StudioSettings? settings = await JsonSerializer.DeserializeAsync<StudioSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return Normalize(settings ?? StudioSettings.Default);
        }
        catch (JsonException)
        {
            return StudioSettings.Default;
        }
    }

    public async Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken)
    {
        StudioSettings normalized = Normalize(settings);
        Directory.CreateDirectory(storagePaths.RootDirectory);

        string tempPath = $"{storagePaths.SettingsPath}.tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, storagePaths.SettingsPath, overwrite: true);
    }

    public async Task<StudioSettings> TouchRecentProjectAsync(
        string projectPath,
        string projectName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        StudioSettings current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        string normalizedPath = Path.GetFullPath(projectPath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RecentProjectEntry entry = new(projectName.Trim(), normalizedPath, now);

        RecentProjectEntry[] updatedRecentProjects =
            [entry, .. current.RecentProjects
                .Where(candidate => !string.Equals(candidate.ProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.LastOpenedAtUtc)
                .Take(RecentProjectLimit - 1)];

        StudioSettings updated = Normalize(current with { RecentProjects = updatedRecentProjects });
        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private static StudioSettings Normalize(StudioSettings settings)
    {
        string? defaultSourceLanguage = NormalizeLanguageCode(settings.DefaultSourceLanguage);
        string? defaultTargetLanguage = NormalizeLanguageCode(settings.DefaultTargetLanguage);
        string modelTierPreference = string.IsNullOrWhiteSpace(settings.ModelTierPreference)
            ? "balanced"
            : settings.ModelTierPreference.Trim().ToLowerInvariant();

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
            DefaultSourceLanguage = defaultSourceLanguage ?? StudioSettings.Default.DefaultSourceLanguage,
            DefaultTargetLanguage = defaultTargetLanguage ?? StudioSettings.Default.DefaultTargetLanguage,
            ModelTierPreference = modelTierPreference,
            WindowLayout = settings.WindowLayout ?? StudioSettings.Default.WindowLayout,
            RecentProjects = recentProjects
        };
    }

    private static string? NormalizeLanguageCode(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? null
            : languageCode.Trim().ToLowerInvariant();
}
