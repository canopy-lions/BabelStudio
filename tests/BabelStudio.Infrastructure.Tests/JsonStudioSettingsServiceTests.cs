using BabelStudio.Application.Contracts;
using BabelStudio.Infrastructure.Settings;

namespace BabelStudio.Infrastructure.Tests;

public sealed class JsonStudioSettingsServiceTests
{
    [Fact]
    public async Task Save_and_load_round_trip_all_fields()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
        var storagePaths = new BabelStudioStoragePaths(tempRoot);
        var service = new JsonStudioSettingsService(storagePaths);
        var settings = new StudioSettings(
            DefaultSourceLanguage: "es",
            DefaultTargetLanguage: "en",
            ModelTierPreference: "quality",
            CommercialSafeMode: false,
            WindowLayout: new WindowLayoutSettings(1600, 900, IsMaximized: true),
            RecentProjects:
            [
                new RecentProjectEntry("One", @"D:\Projects\One.babelstudio", DateTimeOffset.UtcNow)
            ]);

        try
        {
            await service.SaveAsync(settings, CancellationToken.None);
            StudioSettings loaded = await service.LoadAsync(CancellationToken.None);

            Assert.Equal("es", loaded.DefaultSourceLanguage);
            Assert.Equal("en", loaded.DefaultTargetLanguage);
            Assert.Equal("quality", loaded.ModelTierPreference);
            Assert.False(loaded.CommercialSafeMode);
            Assert.Equal(1600, loaded.WindowLayout.Width);
            Assert.Equal(900, loaded.WindowLayout.Height);
            Assert.True(loaded.WindowLayout.IsMaximized);
            Assert.Single(loaded.RecentProjects);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TouchRecentProjectAsync_keeps_only_ten_entries_ordered_newest_first()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
        var service = new JsonStudioSettingsService(new BabelStudioStoragePaths(tempRoot));

        try
        {
            for (int index = 0; index < 11; index++)
            {
                await service.TouchRecentProjectAsync(
                    $@"D:\Projects\Project{index}.babelstudio",
                    $"Project {index}",
                    CancellationToken.None);
            }

            StudioSettings loaded = await service.LoadAsync(CancellationToken.None);

            Assert.Equal(10, loaded.RecentProjects.Count);
            Assert.DoesNotContain(loaded.RecentProjects, entry => entry.ProjectName == "Project 0");
            Assert.Equal("Project 10", loaded.RecentProjects[0].ProjectName);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
