using System.Buffers.Binary;

namespace BabelStudio.Inference.Onnx.Audio;

internal static class WaveAudioReader
{
    public static async Task<AudioSamples> ReadMonoPcm16Async(
        string path,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
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

            long paddedChunkEnd = nextChunk + (chunkSize % 2);
            if (paddedChunkEnd > stream.Length)
            {
                throw new InvalidOperationException("WAV chunk padding exceeded the file length.");
            }

            stream.Position = paddedChunkEnd;
        }

        ValidateHeader(audioFormat, channelCount, sampleRate, blockAlign, bitsPerSample, dataStart, dataLength);

        stream.Position = dataStart;
        byte[] pcmBytes = new byte[dataLength];
        await ReadExactAsync(stream, pcmBytes, cancellationToken).ConfigureAwait(false);

        int sampleFrameCount = dataLength / blockAlign;
        float[] monoSamples = new float[sampleFrameCount];

        for (int frameIndex = 0; frameIndex < sampleFrameCount; frameIndex++)
        {
            int frameOffset = frameIndex * blockAlign;
            double sum = 0;
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                int sampleOffset = frameOffset + (channelIndex * sizeof(short));
                short sample = BinaryPrimitives.ReadInt16LittleEndian(pcmBytes.AsSpan(sampleOffset, sizeof(short)));
                sum += sample / 32768f;
            }

            monoSamples[frameIndex] = (float)(sum / channelCount);
        }

        return new AudioSamples(sampleRate, monoSamples);
    }

    private static void ValidateHeader(
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
    }

    private static void EnsureFourCc(string actualText, string expected)
    {
        if (!string.Equals(actualText, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected WAV marker '{expected}' but found '{actualText}'.");
        }
    }

    private static async Task<string> ReadFourCcAsync(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await ReadExactAsync(stream, buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        return System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
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

internal sealed record AudioSamples(int SampleRate, float[] Samples);
