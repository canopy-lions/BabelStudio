using BabelStudio.Media.Playback;

namespace BabelStudio.TestDoubles;

public sealed class FakePlaybackBackend : IPlaybackBackend, IPlaybackRateBackend
{
    private PlaybackSnapshot snapshot = PlaybackSnapshot.Empty with
    {
        IsLoaded = true,
        Duration = TimeSpan.FromSeconds(5),
        PlaybackRate = 1d
    };

    public string? WarningOnOpen { get; set; }

    public Task OpenAsync(MediaSourceDescriptor source, CancellationToken ct)
    {
        snapshot = snapshot with
        {
            IsLoaded = string.IsNullOrWhiteSpace(WarningOnOpen),
            IsPlaying = false,
            Position = TimeSpan.Zero,
            WarningMessage = WarningOnOpen
        };

        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken ct)
    {
        snapshot = snapshot with { IsPlaying = true };
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct)
    {
        snapshot = snapshot with { IsPlaying = false };
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position, CancellationToken ct)
    {
        snapshot = snapshot with { Position = position };
        return Task.CompletedTask;
    }

    public Task SetPlaybackRateAsync(double playbackRate, CancellationToken ct)
    {
        snapshot = snapshot with { PlaybackRate = playbackRate };
        return Task.CompletedTask;
    }

    public Task<PlaybackSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(snapshot);
}

public sealed class FakePlaybackBackendFactory : IPlaybackBackendFactory
{
    private readonly Dictionary<PlaybackBackendKind, IPlaybackBackend> backends = new();

    public FakePlaybackBackendFactory Add(PlaybackBackendKind backendKind, IPlaybackBackend backend)
    {
        backends[backendKind] = backend;
        return this;
    }

    public IPlaybackBackend? Create(PlaybackBackendKind backendKind) =>
        backends.TryGetValue(backendKind, out IPlaybackBackend? backend)
            ? backend
            : null;
}
