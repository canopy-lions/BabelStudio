namespace BabelStudio.Domain.Media;

public sealed record MediaProbeSnapshot(
    string FormatName,
    string? FormatLongName,
    double DurationSeconds,
    long? BitRate,
    IReadOnlyList<MediaAudioStream> AudioStreams,
    IReadOnlyList<MediaVideoStream> VideoStreams);

public sealed record MediaAudioStream(
    int Index,
    string CodecName,
    int Channels,
    int SampleRate,
    double DurationSeconds);

public sealed record MediaVideoStream(
    int Index,
    string CodecName,
    int Width,
    int Height,
    double FrameRate,
    double DurationSeconds);
