using System.Text.Json;
using System.Text.Json.Serialization;
using BabelStudio.Application.Contracts;
using BabelStudio.Domain.Media;
using BabelStudio.Media.Process;

namespace BabelStudio.Media.Probe;

public sealed class FfmpegMediaProbe : IMediaProbe
{
    private readonly IProcessRunner processRunner;
    private readonly FfmpegToolResolver toolResolver;

    public FfmpegMediaProbe(
        string? ffmpegPath = null,
        string? ffprobePath = null,
        IProcessRunner? processRunner = null)
    {
        this.processRunner = processRunner ?? new ProcessRunner();
        toolResolver = new FfmpegToolResolver(ffmpegPath, ffprobePath);
    }

    public async Task<MediaProbeSnapshot> ProbeAsync(string sourcePath, CancellationToken cancellationToken)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("Source media file was not found.", fullSourcePath);
        }

        string ffprobePath = toolResolver.ResolveFfprobePath();
        ProcessResult result = await processRunner.RunAsync(
            ffprobePath,
            [
                "-v", "quiet",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                fullSourcePath
            ],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffprobe failed with exit code {result.ExitCode}: {result.StandardError}".Trim());
        }

        FfprobePayload? payload = JsonSerializer.Deserialize<FfprobePayload>(
            result.StandardOutput,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload?.Format is null)
        {
            throw new InvalidOperationException("ffprobe did not return format metadata.");
        }

        IReadOnlyList<MediaAudioStream> audioStreams = payload.Streams
            .Where(static stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))
            .Select(static stream => new MediaAudioStream(
                stream.Index,
                stream.CodecName ?? "unknown",
                stream.Channels ?? 0,
                ParseInt(stream.SampleRate),
                ParseDouble(stream.Duration)))
            .ToArray();

        IReadOnlyList<MediaVideoStream> videoStreams = payload.Streams
            .Where(static stream => string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase))
            .Select(static stream => new MediaVideoStream(
                stream.Index,
                stream.CodecName ?? "unknown",
                stream.Width ?? 0,
                stream.Height ?? 0,
                ParseFrameRate(stream.AverageFrameRate),
                ParseDouble(stream.Duration)))
            .ToArray();

        return new MediaProbeSnapshot(
            payload.Format.FormatName ?? "unknown",
            payload.Format.FormatLongName,
            ParseDouble(payload.Format.Duration),
            ParseNullableLong(payload.Format.BitRate),
            audioStreams,
            videoStreams);
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, out int parsed) ? parsed : 0;

    private static double ParseDouble(string? value) =>
        double.TryParse(value, out double parsed) ? parsed : 0d;

    private static long? ParseNullableLong(string? value) =>
        long.TryParse(value, out long parsed) ? parsed : null;

    private static double ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0d;
        }

        string[] parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], out double numerator) &&
            double.TryParse(parts[1], out double denominator) &&
            denominator != 0d)
        {
            return numerator / denominator;
        }

        return ParseDouble(value);
    }

    private sealed record FfprobePayload(
        [property: JsonPropertyName("format")] FfprobeFormat? Format,
        [property: JsonPropertyName("streams")] IReadOnlyList<FfprobeStream> Streams)
    {
        public FfprobePayload()
            : this(null, [])
        {
        }
    }

    private sealed record FfprobeFormat(
        [property: JsonPropertyName("format_name")] string? FormatName,
        [property: JsonPropertyName("format_long_name")] string? FormatLongName,
        [property: JsonPropertyName("duration")] string? Duration,
        [property: JsonPropertyName("bit_rate")] string? BitRate);

    private sealed record FfprobeStream(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("codec_type")] string? CodecType,
        [property: JsonPropertyName("codec_name")] string? CodecName,
        [property: JsonPropertyName("sample_rate")] string? SampleRate,
        [property: JsonPropertyName("channels")] int? Channels,
        [property: JsonPropertyName("duration")] string? Duration,
        [property: JsonPropertyName("avg_frame_rate")] string? AverageFrameRate,
        [property: JsonPropertyName("width")] int? Width,
        [property: JsonPropertyName("height")] int? Height);
}
