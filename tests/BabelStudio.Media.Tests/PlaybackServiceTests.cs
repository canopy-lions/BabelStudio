using BabelStudio.TestDoubles;
using BabelStudio.Domain.Media;
using BabelStudio.Media.Playback;

namespace BabelStudio.Media.Tests;

public sealed class PlaybackCapabilityProbeTests
{
    [Fact]
    public void Assess_prefers_media_foundation_for_common_mp4()
    {
        var probe = new PlaybackCapabilityProbe();
        var source = new MediaSourceDescriptor(
            @"D:\media\sample.mp4",
            new MediaProbeSnapshot(
                "mov,mp4,m4a,3gp,3g2,mj2",
                "QuickTime / MOV",
                12.0,
                2048,
                [new MediaAudioStream(0, "aac", 2, 44100, 12.0)],
                [new MediaVideoStream(1, "h264", 1920, 1080, 24.0, 12.0)]));

        PlaybackCapabilityAssessment assessment = probe.Assess(source);

        Assert.Equal(PlaybackBackendKind.MediaFoundation, assessment.PreferredBackend);
        Assert.True(assessment.IsLikelySupportedByCurrentWindowsMediaStack);
        Assert.Equal("h264", assessment.VideoCodec);
        Assert.Equal("aac", assessment.AudioCodec);
    }

    [Fact]
    public void Assess_routes_uncommon_container_to_fallback_and_flags_hdr()
    {
        var probe = new PlaybackCapabilityProbe();
        var source = new MediaSourceDescriptor(
            @"D:\media\sample.mkv",
            new MediaProbeSnapshot(
                "matroska,webm",
                "Matroska / WebM",
                12.0,
                4096,
                [new MediaAudioStream(0, "opus", 2, 48000, 12.0)],
                [new MediaVideoStream(1, "prores", 1920, 1080, 24.0, 12.0, ColorTransfer: "smpte2084")],
                [new MediaSubtitleStream(2, "ass", "eng")]));

        PlaybackCapabilityAssessment assessment = probe.Assess(source);

        Assert.Equal(PlaybackBackendKind.FfmpegFallback, assessment.PreferredBackend);
        Assert.False(assessment.IsLikelySupportedByCurrentWindowsMediaStack);
        Assert.True(assessment.IsHdrLikely);
        Assert.Equal(1, assessment.SubtitleTrackCount);
        Assert.Contains("Windows native playback is unlikely", assessment.WarningMessage);
    }
}

public sealed class PlaybackServiceTests
{
    [Fact]
    public async Task Open_seek_and_rate_change_flow_through_selected_backend()
    {
        var backend = new FakePlaybackBackend();
        var service = new PlaybackService(
            new PlaybackCapabilityProbe(),
            new FakePlaybackBackendFactory().Add(PlaybackBackendKind.MediaFoundation, backend));
        var source = new MediaSourceDescriptor(
            @"D:\media\sample.mp4",
            new MediaProbeSnapshot(
                "mp4",
                "MP4",
                5.0,
                2048,
                [new MediaAudioStream(0, "aac", 2, 44100, 5.0)],
                [new MediaVideoStream(1, "h264", 1280, 720, 24.0, 5.0)]));

        PlaybackOpenResult openResult = await service.OpenAsync(source, CancellationToken.None);
        await service.SeekAsync(TimeSpan.FromSeconds(2.5), CancellationToken.None);
        await service.SetPlaybackRateAsync(1.25, CancellationToken.None);
        await service.PlayAsync(CancellationToken.None);
        PlaybackSnapshot snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.True(openResult.IsBackendAvailable);
        Assert.Equal(TimeSpan.FromSeconds(2.5), snapshot.Position);
        Assert.Equal(1.25, snapshot.PlaybackRate);
        Assert.True(snapshot.IsPlaying);
    }

    [Fact]
    public void WaveformMapping_converts_time_and_pixels_consistently()
    {
        float x = WaveformMapping.TimeToPixel(5.0, 10.0, 200f);
        double seconds = WaveformMapping.PixelToTime(x, 10.0, 200f);

        Assert.Equal(100f, x, 3);
        Assert.Equal(5.0, seconds, 3);
    }

    private sealed class FakePlaybackBackendFactory : IPlaybackBackendFactory
    {
        private readonly IPlaybackBackend backend;

        public FakePlaybackBackendFactory(IPlaybackBackend backend)
        {
            this.backend = backend;
        }

        public IPlaybackBackend? Create(PlaybackBackendKind backendKind) =>
            backendKind == PlaybackBackendKind.MediaFoundation ? backend : null;
    }

    private sealed class FakePlaybackBackend : IPlaybackBackend, IPlaybackRateBackend
    {
        private PlaybackSnapshot snapshot = PlaybackSnapshot.Empty with
        {
            IsLoaded = true,
            Duration = TimeSpan.FromSeconds(5)
        };

        public Task OpenAsync(MediaSourceDescriptor source, CancellationToken ct) => Task.CompletedTask;

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
}
