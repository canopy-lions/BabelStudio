#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace BabelStudio.Media.Playback;

public sealed class MediaFoundationPlaybackBackend :
    IPlaybackBackend,
    IPlaybackHostAwareBackend,
    IPlaybackRateBackend,
    IDisposable
{
    private MediaPlayer? mediaPlayer;
    private MediaPlayerElement? mediaPlayerElement;

    public bool TryAttachHost(object host)
    {
        if (host is not MediaPlayerElement element)
        {
            return false;
        }

        mediaPlayerElement = element;
        if (mediaPlayer is not null)
        {
            mediaPlayerElement.SetMediaPlayer(mediaPlayer);
        }

        return true;
    }

    public Task OpenAsync(MediaSourceDescriptor source, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourcePath);

        MediaPlayer player = EnsurePlayer();
        player.Source = MediaSource.CreateFromUri(new Uri(source.SourcePath, UriKind.Absolute));
        player.PlaybackSession.Position = TimeSpan.Zero;
        player.PlaybackSession.PlaybackRate = 1d;
        player.Pause();
        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken ct)
    {
        mediaPlayer?.Play();
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct)
    {
        mediaPlayer?.Pause();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position, CancellationToken ct)
    {
        if (mediaPlayer is not null)
        {
            mediaPlayer.PlaybackSession.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }

        return Task.CompletedTask;
    }

    public Task SetPlaybackRateAsync(double playbackRate, CancellationToken ct)
    {
        if (mediaPlayer is not null)
        {
            mediaPlayer.PlaybackSession.PlaybackRate = playbackRate <= 0d ? 1d : playbackRate;
        }

        return Task.CompletedTask;
    }

    public Task<PlaybackSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        if (mediaPlayer is null)
        {
            return Task.FromResult(PlaybackSnapshot.Empty);
        }

        MediaPlaybackSession session = mediaPlayer.PlaybackSession;
        return Task.FromResult(new PlaybackSnapshot(
            IsLoaded: mediaPlayer.Source is not null,
            IsPlaying: session.PlaybackState == MediaPlaybackState.Playing,
            Position: session.Position,
            Duration: session.NaturalDuration,
            PlaybackRate: session.PlaybackRate));
    }

    public void Dispose()
    {
        mediaPlayerElement?.SetMediaPlayer(null);
        mediaPlayer?.Dispose();
        mediaPlayer = null;
    }

    private MediaPlayer EnsurePlayer()
    {
        if (mediaPlayer is not null)
        {
            return mediaPlayer;
        }

        mediaPlayer = new MediaPlayer
        {
            AutoPlay = false,
            IsLoopingEnabled = false
        };

        if (mediaPlayerElement is not null)
        {
            mediaPlayerElement.SetMediaPlayer(mediaPlayer);
        }

        return mediaPlayer;
    }
}
#endif
