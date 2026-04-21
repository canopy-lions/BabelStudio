using BabelStudio.Application.Contracts;
using BabelStudio.Media.Process;
using BabelStudio.Media.Waveforms;

namespace BabelStudio.Media.Extraction;

public sealed class FfmpegAudioExtractionService : IAudioExtractionService
{
    private readonly IProcessRunner processRunner;
    private readonly FfmpegToolResolver toolResolver;

    public FfmpegAudioExtractionService(string? ffmpegPath = null)
        : this(new ProcessRunner(), ffmpegPath)
    {
    }

    internal FfmpegAudioExtractionService(IProcessRunner processRunner, string? ffmpegPath = null)
    {
        this.processRunner = processRunner;
        toolResolver = new FfmpegToolResolver(ffmpegPath);
    }
    public async Task<AudioExtractionResult> ExtractNormalizedAudioAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("Source media file was not found.", fullSourcePath);
        }

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestinationPath)!);
        if (File.Exists(fullDestinationPath))
        {
            File.Delete(fullDestinationPath);
        }

        ProcessResult result = await processRunner.RunAsync(
            toolResolver.ResolveFfmpegPath(),
            [
                "-y",
                "-hide_banner",
                "-loglevel", "error",
                "-i", fullSourcePath,
                "-vn",
                "-sn",
                "-dn",
                "-map", "0:a:0",
                "-ac", "1",
                "-ar", "48000",
                "-c:a", "pcm_s16le",
                fullDestinationPath
            ],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg audio extraction failed with exit code {result.ExitCode}: {result.StandardError}".Trim());
        }

        if (!File.Exists(fullDestinationPath))
        {
            throw new InvalidOperationException("ffmpeg completed without producing a normalized audio file.");
        }

        WavePcm16Info waveInfo = await WavePcm16.ReadInfoAsync(fullDestinationPath, cancellationToken).ConfigureAwait(false);
        return new AudioExtractionResult(
            fullDestinationPath,
            waveInfo.DurationSeconds,
            waveInfo.SampleRate,
            waveInfo.ChannelCount,
            waveInfo.SampleFrames);
    }
}
