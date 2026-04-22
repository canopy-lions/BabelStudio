using BabelStudio.Application.Contracts;
using BabelStudio.Infrastructure.Settings;
using BabelStudio.Media.Playback;

namespace BabelStudio.Composition;

public sealed class StudioShellFactory
{
    public StudioShellServices Create()
    {
        var storagePaths = new BabelStudioStoragePaths();
        return new StudioShellServices(
            new JsonStudioSettingsService(storagePaths),
            new PlaybackService(
                new PlaybackCapabilityProbe(),
                new DefaultPlaybackBackendFactory()));
    }
}

public sealed record StudioShellServices(
    IStudioSettingsService SettingsService,
    PlaybackService PlaybackService);
