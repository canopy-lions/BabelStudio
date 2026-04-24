namespace BabelStudio.Application.Contracts;

public interface IAudioClipExtractor
{
    Task<AudioClipExtractionResult> ExtractAsync(
        string sourceWavePath,
        double startSeconds,
        double endSeconds,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed record AudioClipExtractionResult(
    string OutputPath,
    double DurationSeconds,
    int SampleRate,
    int ChannelCount);
