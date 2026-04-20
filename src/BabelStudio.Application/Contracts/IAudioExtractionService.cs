namespace BabelStudio.Application.Contracts;

public interface IAudioExtractionService
{
    Task<AudioExtractionResult> ExtractNormalizedAudioAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed record AudioExtractionResult(
    string OutputPath,
    double DurationSeconds,
    int SampleRate,
    int ChannelCount,
    long SampleFrames);
