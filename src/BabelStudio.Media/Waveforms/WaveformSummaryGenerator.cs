using BabelStudio.Application.Contracts;

namespace BabelStudio.Media.Waveforms;

public sealed class WaveformSummaryGenerator : IWaveformSummaryGenerator
{
    private readonly int bucketCount;

    public WaveformSummaryGenerator(int bucketCount = 128)
    {
        if (bucketCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketCount));
        }

        this.bucketCount = bucketCount;
    }

    public Task<WaveformSummary> GenerateAsync(string audioPath, CancellationToken cancellationToken)
    {
        string fullAudioPath = Path.GetFullPath(audioPath);
        if (!File.Exists(fullAudioPath))
        {
            throw new FileNotFoundException("Audio file was not found for waveform generation.", fullAudioPath);
        }

        WavePcm16Info waveInfo = WavePcm16.ReadInfo(fullAudioPath);
        float[] peaks = new float[bucketCount];
        long framesPerBucket = Math.Max(1, (long)Math.Ceiling((double)waveInfo.SampleFrames / bucketCount));

        using var stream = File.OpenRead(fullAudioPath);
        using var reader = new BinaryReader(stream);
        stream.Position = waveInfo.DataStartPosition;

        for (long frameIndex = 0; frameIndex < waveInfo.SampleFrames; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float framePeak = 0f;
            for (int channel = 0; channel < waveInfo.ChannelCount; channel++)
            {
                short sample = reader.ReadInt16();
                float magnitude = Math.Abs(sample / 32768f);
                if (magnitude > framePeak)
                {
                    framePeak = magnitude;
                }
            }

            int bucketIndex = (int)Math.Min(bucketCount - 1, frameIndex / framesPerBucket);
            if (framePeak > peaks[bucketIndex])
            {
                peaks[bucketIndex] = framePeak;
            }
        }

        return Task.FromResult(
            new WaveformSummary(
                bucketCount,
                waveInfo.SampleRate,
                waveInfo.ChannelCount,
                waveInfo.DurationSeconds,
                peaks));
    }
}
