using System.Buffers.Binary;
using System.Text;

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
    public static async Task<WavePcm16Info> ReadInfoAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer4 = new byte[4];
        byte[] buffer2 = new byte[2];

        EnsureFourCc(await ReadFourCcAsync(stream, buffer4, cancellationToken).ConfigureAwait(false), "RIFF");
        _ = await ReadInt32Async(stream, buffer4, cancellationToken).ConfigureAwait(false);
        EnsureFourCc(await ReadFourCcAsync(stream, buffer4, cancellationToken).ConfigureAwait(false), "WAVE");

        ushort audioFormat = 0;
        ushort channelCount = 0;
        int sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        long dataStart = 0;
        int dataLength = 0;

        while (stream.Position < stream.Length)
        {
            string chunkId = await ReadFourCcAsync(stream, buffer4, cancellationToken).ConfigureAwait(false);
            int chunkSize = await ReadInt32Async(stream, buffer4, cancellationToken).ConfigureAwait(false);
            long nextChunk = stream.Position + chunkSize;

            switch (chunkId)
            {
                case "fmt ":
                    audioFormat = await ReadUInt16Async(stream, buffer2, cancellationToken).ConfigureAwait(false);
                    channelCount = await ReadUInt16Async(stream, buffer2, cancellationToken).ConfigureAwait(false);
                    sampleRate = await ReadInt32Async(stream, buffer4, cancellationToken).ConfigureAwait(false);
                    _ = await ReadInt32Async(stream, buffer4, cancellationToken).ConfigureAwait(false);
                    blockAlign = await ReadUInt16Async(stream, buffer2, cancellationToken).ConfigureAwait(false);
                    bitsPerSample = await ReadUInt16Async(stream, buffer2, cancellationToken).ConfigureAwait(false);
                    break;

                case "data":
                    dataStart = stream.Position;
                    dataLength = chunkSize;
                    break;
            }

            stream.Position = nextChunk + (chunkSize % 2);
        }

        return CreateInfo(audioFormat, channelCount, sampleRate, blockAlign, bitsPerSample, dataStart, dataLength);
    }

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

        return CreateInfo(audioFormat, channelCount, sampleRate, blockAlign, bitsPerSample, dataStart, dataLength);
    }

    private static void EnsureFourCc(string actualText, string expected)
    {
        if (!string.Equals(actualText, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected WAV marker '{expected}' but found '{actualText}'.");
        }
    }

    private static void EnsureFourCc(char[] actual, string expected)
    {
        EnsureFourCc(new string(actual), expected);
    }

    private static WavePcm16Info CreateInfo(
        ushort audioFormat,
        ushort channelCount,
        int sampleRate,
        ushort blockAlign,
        ushort bitsPerSample,
        long dataStart,
        int dataLength)
    {
        if (audioFormat != 1)
        {
            throw new InvalidOperationException($"Unsupported WAV encoding '{audioFormat}'. Only PCM is supported.");
        }

        if (bitsPerSample != 16)
        {
            throw new InvalidOperationException($"Unsupported WAV bit depth '{bitsPerSample}'. Only 16-bit PCM is supported.");
        }

        if (channelCount == 0 || sampleRate <= 0 || blockAlign == 0)
        {
            throw new InvalidOperationException("WAV header contains invalid channel count, sample rate, or block alignment.");
        }

        if (dataStart == 0 || dataLength == 0)
        {
            throw new InvalidOperationException("WAV file does not contain a data chunk.");
        }

        long sampleFrames = dataLength / blockAlign;
        return new WavePcm16Info(sampleRate, channelCount, bitsPerSample, blockAlign, dataStart, dataLength, sampleFrames);
    }

    private static async Task<string> ReadFourCcAsync(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await ReadExactAsync(stream, buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        return Encoding.ASCII.GetString(buffer, 0, 4);
    }

    private static async Task<ushort> ReadUInt16Async(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await ReadExactAsync(stream, buffer.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    private static async Task<int> ReadInt32Async(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await ReadExactAsync(stream, buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static async Task ReadExactAsync(
        FileStream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer[totalBytesRead..], cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Unexpected end of WAV header.");
            }

            totalBytesRead += bytesRead;
        }
    }
}
