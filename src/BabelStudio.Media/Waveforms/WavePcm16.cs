namespace BabelStudio.Media.Waveforms;

internal sealed record WavePcm16Info(
    int SampleRate,
    int ChannelCount,
    int BitsPerSample,
    int BlockAlign,
    long DataStartPosition,
    long DataLengthBytes,
    long SampleFrames)
{
    public double DurationSeconds => SampleRate == 0 ? 0 : (double)SampleFrames / SampleRate;
}

internal static class WavePcm16
{
    public static WavePcm16Info ReadInfo(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        EnsureFourCc(reader.ReadChars(4), "RIFF");
        _ = reader.ReadInt32();
        EnsureFourCc(reader.ReadChars(4), "WAVE");

        ushort audioFormat = 0;
        ushort channelCount = 0;
        int sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        long dataStart = 0;
        int dataLength = 0;

        while (stream.Position < stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            long nextChunk = stream.Position + chunkSize;

            switch (chunkId)
            {
                case "fmt ":
                    audioFormat = reader.ReadUInt16();
                    channelCount = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();
                    _ = reader.ReadInt32();
                    blockAlign = reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    break;

                case "data":
                    dataStart = stream.Position;
                    dataLength = chunkSize;
                    break;
            }

            stream.Position = nextChunk + (chunkSize % 2);
        }

        if (audioFormat != 1)
        {
            throw new InvalidOperationException($"Unsupported WAV encoding '{audioFormat}'. Only PCM is supported.");
        }

        if (bitsPerSample != 16)
        {
            throw new InvalidOperationException($"Unsupported WAV bit depth '{bitsPerSample}'. Only 16-bit PCM is supported.");
        }

        if (dataStart == 0 || dataLength == 0)
        {
            throw new InvalidOperationException("WAV file does not contain a data chunk.");
        }

        long sampleFrames = dataLength / blockAlign;
        return new WavePcm16Info(sampleRate, channelCount, bitsPerSample, blockAlign, dataStart, dataLength, sampleFrames);
    }

    private static void EnsureFourCc(char[] actual, string expected)
    {
        string actualText = new(actual);
        if (!string.Equals(actualText, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected WAV marker '{expected}' but found '{actualText}'.");
        }
    }
}
