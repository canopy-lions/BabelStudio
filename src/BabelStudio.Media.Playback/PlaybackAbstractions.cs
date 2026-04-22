using BabelStudio.Application.Contracts;
using BabelStudio.Domain;
using BabelStudio.Domain.Media;

namespace BabelStudio.Media.Playback;

public enum PlaybackBackendKind
{
    MediaFoundation = 0,
    FfmpegFallback = 1,
    LibMpvFallback = 2
}

public sealed record MediaSourceDescriptor(
    string SourcePath,
    MediaProbeSnapshot Probe);

public sealed record PlaybackSnapshot(
    bool IsLoaded,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    double PlaybackRate,
    string? WarningMessage)
{
    public static PlaybackSnapshot Empty { get; } = new(
        IsLoaded: false,
        IsPlaying: false,
        Position: TimeSpan.Zero,
        Duration: TimeSpan.Zero,
        PlaybackRate: 1d,
        WarningMessage: null);
}

public sealed record PlaybackCapabilityAssessment(
    PlaybackBackendKind PreferredBackend,
    bool IsLikelySupportedByCurrentWindowsMediaStack,
    string ContainerName,
    string? VideoCodec,
    string? AudioCodec,
    int SubtitleTrackCount,
    bool IsHdrLikely,
    string? WarningMessage);

public sealed class PlaybackCapabilityProbe
{
    private static readonly HashSet<string> MediaFoundationContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4",
        "mov",
        "m4v",
        "m4a",
        "mp3",
        "wav",
        "aac",
        "flac"
    };

    private static readonly HashSet<string> MediaFoundationVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "h264",
        "avc1",
        "hevc",
        "h265",
        "av1",
        "vp9"
    };

    private static readonly HashSet<string> MediaFoundationAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac",
        "mp3",
        "ac3",
        "eac3",
        "flac",
        "alac",
        "pcm_s16le",
        "pcm_s24le",
        "pcm_f32le"
    };

    public PlaybackCapabilityAssessment Assess(MediaSourceDescriptor source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourcePath);
        ArgumentNullException.ThrowIfNull(source.Probe);

        MediaVideoStream? primaryVideo = source.Probe.VideoStreams.FirstOrDefault();
        MediaAudioStream? primaryAudio = source.Probe.AudioStreams.FirstOrDefault();
        string containerName = SelectPrimaryContainer(source.Probe.FormatName);
        string? videoCodec = Normalize(primaryVideo?.CodecName);
        string? audioCodec = Normalize(primaryAudio?.CodecName);
        int subtitleTrackCount = source.Probe.SubtitleStreams?.Count ?? 0;
        bool isHdrLikely = primaryVideo is not null &&
                           IsHdrTransfer(primaryVideo.ColorTransfer);

        bool hasSupportedContainer = MediaFoundationContainers.Contains(containerName);
        bool hasSupportedVideo = primaryVideo is null || MediaFoundationVideoCodecs.Contains(videoCodec ?? string.Empty);
        bool hasSupportedAudio = primaryAudio is null || MediaFoundationAudioCodecs.Contains(audioCodec ?? string.Empty);
        bool prefersMediaFoundation = hasSupportedContainer && hasSupportedVideo && hasSupportedAudio;

        string? warning = prefersMediaFoundation
            ? BuildSoftWarning(subtitleTrackCount, isHdrLikely)
            : BuildFallbackWarning(containerName, videoCodec, audioCodec, subtitleTrackCount, isHdrLikely);

        return new PlaybackCapabilityAssessment(
            prefersMediaFoundation ? PlaybackBackendKind.MediaFoundation : PlaybackBackendKind.FfmpegFallback,
            prefersMediaFoundation,
            containerName,
            videoCodec,
            audioCodec,
            subtitleTrackCount,
            isHdrLikely,
            warning);
    }

    private static string SelectPrimaryContainer(string formatName)
    {
        string firstContainer = (formatName ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "unknown";
        return Normalize(firstContainer) ?? "unknown";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static bool IsHdrTransfer(string? colorTransfer)
    {
        string? normalized = Normalize(colorTransfer);
        return normalized is "smpte2084" or "arib-std-b67";
    }

    private static string? BuildSoftWarning(int subtitleTrackCount, bool isHdrLikely)
    {
        List<string> warnings = [];
        if (subtitleTrackCount > 0)
        {
            warnings.Add($"{subtitleTrackCount} embedded subtitle track(s) detected.");
        }

        if (isHdrLikely)
        {
            warnings.Add("HDR metadata detected.");
        }

        return warnings.Count == 0 ? null : string.Join(' ', warnings);
    }

    private static string BuildFallbackWarning(
        string containerName,
        string? videoCodec,
        string? audioCodec,
        int subtitleTrackCount,
        bool isHdrLikely)
    {
        List<string> reasons =
        [
            $"Windows native playback is unlikely to support this source cleanly ({containerName}, video={videoCodec ?? "none"}, audio={audioCodec ?? "none"})."
        ];

        if (subtitleTrackCount > 0)
        {
            reasons.Add($"{subtitleTrackCount} embedded subtitle track(s) detected.");
        }

        if (isHdrLikely)
        {
            reasons.Add("HDR metadata detected.");
        }

        return string.Join(' ', reasons);
    }
}

public interface IPlaybackBackend
{
    Task OpenAsync(MediaSourceDescriptor source, CancellationToken ct);

    Task PlayAsync(CancellationToken ct);

    Task PauseAsync(CancellationToken ct);

    Task SeekAsync(TimeSpan position, CancellationToken ct);

    Task<PlaybackSnapshot> GetSnapshotAsync(CancellationToken ct);
}

public interface IPlaybackHostAwareBackend
{
    bool TryAttachHost(object host);
}

public interface IPlaybackRateBackend
{
    Task SetPlaybackRateAsync(double playbackRate, CancellationToken ct);
}

public interface IPlaybackBackendFactory
{
    IPlaybackBackend? Create(PlaybackBackendKind backendKind);
}

public sealed record PlaybackOpenResult(
    PlaybackCapabilityAssessment Assessment,
    bool IsBackendAvailable,
    PlaybackSnapshot Snapshot);

public sealed class PlaybackService
{
    private readonly PlaybackCapabilityProbe capabilityProbe;
    private readonly IPlaybackBackendFactory backendFactory;
    private IPlaybackBackend? backend;

    public PlaybackService(
        PlaybackCapabilityProbe capabilityProbe,
        IPlaybackBackendFactory backendFactory)
    {
        this.capabilityProbe = capabilityProbe;
        this.backendFactory = backendFactory;
    }

    public PlaybackCapabilityAssessment? CurrentAssessment { get; private set; }

    public bool TryAttachHost(object host)
    {
        return backend is IPlaybackHostAwareBackend hostAwareBackend &&
               hostAwareBackend.TryAttachHost(host);
    }

    public async Task<PlaybackOpenResult> OpenAsync(MediaSourceDescriptor source, CancellationToken ct)
    {
        CurrentAssessment = capabilityProbe.Assess(source);
        ReplaceBackend(backendFactory.Create(CurrentAssessment.PreferredBackend));
        if (backend is null)
        {
            return new PlaybackOpenResult(
                CurrentAssessment,
                IsBackendAvailable: false,
                PlaybackSnapshot.Empty with
                {
                    WarningMessage = BuildBackendUnavailableWarning(CurrentAssessment)
                });
        }

        await backend.OpenAsync(source, ct).ConfigureAwait(false);
        PlaybackSnapshot snapshot = await backend.GetSnapshotAsync(ct).ConfigureAwait(false);
        return new PlaybackOpenResult(CurrentAssessment, IsBackendAvailable: true, snapshot);
    }

    public Task PlayAsync(CancellationToken ct) =>
        backend is null ? Task.CompletedTask : backend.PlayAsync(ct);

    public Task PauseAsync(CancellationToken ct) =>
        backend is null ? Task.CompletedTask : backend.PauseAsync(ct);

    public Task SeekAsync(TimeSpan position, CancellationToken ct) =>
        backend is null ? Task.CompletedTask : backend.SeekAsync(position, ct);

    public Task SetPlaybackRateAsync(double playbackRate, CancellationToken ct)
    {
        return backend is IPlaybackRateBackend rateBackend
            ? rateBackend.SetPlaybackRateAsync(playbackRate, ct)
            : Task.CompletedTask;
    }

    public Task<PlaybackSnapshot> GetSnapshotAsync(CancellationToken ct) =>
        backend is null
            ? Task.FromResult(PlaybackSnapshot.Empty)
            : backend.GetSnapshotAsync(ct);

    public void Reset()
    {
        CurrentAssessment = null;
        ReplaceBackend(null);
    }

    private void ReplaceBackend(IPlaybackBackend? nextBackend)
    {
        if (!ReferenceEquals(backend, nextBackend) &&
            backend is IDisposable disposableBackend)
        {
            disposableBackend.Dispose();
        }

        backend = nextBackend;
    }

    private static string BuildBackendUnavailableWarning(PlaybackCapabilityAssessment assessment) =>
        assessment.PreferredBackend switch
        {
            PlaybackBackendKind.FfmpegFallback =>
                "FFmpeg fallback is required for this source, but that backend is not implemented in this build.",
            PlaybackBackendKind.LibMpvFallback =>
                "libmpv fallback is required for this source, but that backend is not implemented in this build.",
            PlaybackBackendKind.MediaFoundation =>
                "Media Foundation playback is not available in this build.",
            _ =>
                "The selected playback backend is not available in this build."
        };
}

public sealed record WaveformSegmentBoundary(
    double StartSeconds,
    double EndSeconds);

public sealed record WaveformBarLayout(
    float X,
    float TopY,
    float BottomY,
    float StrokeWidth);

public sealed record WaveformCanvasLayout(
    IReadOnlyList<WaveformBarLayout> Bars,
    IReadOnlyList<float> SegmentStartMarkerXs,
    IReadOnlyList<float> SegmentEndMarkerXs,
    float CursorX);

public static class WaveformLayout
{
    public static WaveformCanvasLayout Build(
        WaveformSummary waveform,
        IReadOnlyList<WaveformSegmentBoundary> segments,
        double playbackPositionSeconds,
        float width,
        float height)
    {
        ArgumentNullException.ThrowIfNull(waveform);
        ArgumentNullException.ThrowIfNull(segments);

        if (waveform.Peaks.Count == 0 || width <= 0f || height <= 0f)
        {
            return new WaveformCanvasLayout(
                Array.Empty<WaveformBarLayout>(),
                Array.Empty<float>(),
                Array.Empty<float>(),
                0f);
        }

        float centerY = height / 2f;
        float step = width / Math.Max(waveform.Peaks.Count, 1);
        var bars = new List<WaveformBarLayout>(waveform.Peaks.Count);

        for (int index = 0; index < waveform.Peaks.Count; index++)
        {
            float amplitude = Math.Clamp(waveform.Peaks[index], 0f, 1f);
            float barHeight = Math.Max(1f, amplitude * height);
            float x = index * step;
            bars.Add(new WaveformBarLayout(
                x,
                centerY - (barHeight / 2f),
                centerY + (barHeight / 2f),
                Math.Max(1f, step * 0.6f)));
        }

        float[] startMarkers = segments
            .Select(segment => WaveformMapping.TimeToPixel(segment.StartSeconds, waveform.DurationSeconds, width))
            .ToArray();
        float[] endMarkers = segments
            .Select(segment => WaveformMapping.TimeToPixel(segment.EndSeconds, waveform.DurationSeconds, width))
            .ToArray();
        float cursorX = WaveformMapping.TimeToPixel(playbackPositionSeconds, waveform.DurationSeconds, width);

        return new WaveformCanvasLayout(bars, startMarkers, endMarkers, cursorX);
    }
}

public static class WaveformMapping
{
    public static float TimeToPixel(double timeSeconds, double durationSeconds, float width)
    {
        if (!double.IsFinite(timeSeconds) || !double.IsFinite(durationSeconds) || durationSeconds <= 0d || width <= 0f)
        {
            return 0f;
        }

        double ratio = Math.Clamp(timeSeconds / durationSeconds, 0d, 1d);
        return (float)(ratio * width);
    }

    public static double PixelToTime(float pixel, double durationSeconds, float width)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0d || width <= 0f)
        {
            return 0d;
        }

        double ratio = Math.Clamp(pixel / width, 0f, 1f);
        return ratio * durationSeconds;
    }
}

public sealed class DefaultPlaybackBackendFactory : IPlaybackBackendFactory
{
    public IPlaybackBackend? Create(PlaybackBackendKind backendKind)
    {
#if WINDOWS
        return backendKind == PlaybackBackendKind.MediaFoundation
            ? new MediaFoundationPlaybackBackend()
            : null;
#else
        return null;
#endif
    }
}
