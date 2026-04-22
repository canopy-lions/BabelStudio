using BabelStudio.Application.Contracts;
using BabelStudio.TestDoubles;

namespace BabelStudio.Infrastructure.Tests;

public sealed class FakeStudioSettingsServiceTests
{
    [Fact]
    public async Task TouchRecentProjectAsync_keeps_only_ten_entries_ordered_newest_first()
    {
        var service = new FakeStudioSettingsService();

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
}
