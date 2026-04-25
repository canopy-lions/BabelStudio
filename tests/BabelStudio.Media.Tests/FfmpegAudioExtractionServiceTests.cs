using BabelStudio.Media.Extraction;
using BabelStudio.Media.Waveforms;

namespace BabelStudio.Media.Tests;

public sealed class FfmpegAudioExtractionServiceTests
{
    [RequiresFfmpegFact]
    public async Task ExtractNormalizedAudioAsync_creates_mono_wav_output_and_waveform()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BabelStudio.Media.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string sourcePath = await MediaFixtureFactory.CreateSampleVideoAsync(tempDirectory);
            string outputPath = Path.Combine(tempDirectory, "normalized.wav");
            var service = new FfmpegAudioExtractionService();
            var waveformGenerator = new WaveformSummaryGenerator(bucketCount: 16);

            var result = await service.ExtractNormalizedAudioAsync(sourcePath, outputPath, CancellationToken.None);
            var waveform = await waveformGenerator.GenerateAsync(outputPath, CancellationToken.None);

            WavePcm16Info waveInfo = await WavePcm16.ReadInfoAsync(outputPath, CancellationToken.None);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(outputPath, result.OutputPath);
            Assert.Equal(48000, result.SampleRate);
            Assert.Equal(1, result.ChannelCount);
            Assert.True(result.DurationSeconds > 1d);
            Assert.Equal(48000, waveInfo.SampleRate);
            Assert.Equal(1, waveInfo.ChannelCount);
            Assert.Equal(16, waveform.BucketCount);
            Assert.Equal(48000, waveform.SampleRate);
            Assert.Equal(1, waveform.ChannelCount);
            Assert.Contains(waveform.Peaks, static peak => peak > 0f);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
