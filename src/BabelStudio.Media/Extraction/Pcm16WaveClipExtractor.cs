using System.Buffers.Binary;
using BabelStudio.Application.Contracts;

namespace BabelStudio.Media.Extraction;

public sealed class Pcm16WaveClipExtractor : IAudioClipExtractor
{
    public async Task<AudioClipExtractionResult> ExtractAsync(
        string sourceWavePath,
        double startSeconds,
        double endSeconds,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(sourceWavePath))
        {
            throw new FileNotFoundException("Source wave file was not found.", sourceWavePath);
        }

        if (!double.IsFinite(startSeconds) || startSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Clip start must be finite and non-negative.");
        }

        if (!double.IsFinite(endSeconds) || endSeconds <= startSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Clip end must be finite and greater than the start.");
        }

        byte[] sourceBytes = await File.ReadAllBytesAsync(sourceWavePath, cancellationToken).ConfigureAwait(false);
        WaveClipSource source = ParseWave(sourceBytes);
        int bytesPerFrame = source.BlockAlign;
        long startFrame = Math.Max(0L, (long)Math.Floor(startSeconds * source.SampleRate));
        long endFrame = Math.Min(source.FrameCount, (long)Math.Ceiling(endSeconds * source.SampleRate));
        if (endFrame <= startFrame)
        {
            throw new InvalidOperationException("Clip range does not contain any audio frames.");
        }

        int clipDataLength = checked((int)((endFrame - startFrame) * bytesPerFrame));
        int dataOffset = checked(source.DataOffset + (int)(startFrame * bytesPerFrame));
        byte[] clipData = new byte[clipDataLength];
        Buffer.BlockCopy(sourceBytes, dataOffset, clipData, 0, clipDataLength);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using FileStream destination = File.Create(destinationPath);
        await WriteWaveAsync(destination, source.SampleRate, source.ChannelCount, source.BitsPerSample, clipData, cancellationToken).ConfigureAwait(false);

        return new AudioClipExtractionResult(
            destinationPath,
            (endFrame - startFrame) / (double)source.SampleRate,
            source.SampleRate,
            source.ChannelCount);
    }

    private static WaveClipSource ParseWave(byte[] bytes)
    {
        if (bytes.Length < 44 ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidOperationException("Only RIFF/WAVE audio is supported for reference clip extraction.");
        }

        int offset = 12;
        int sampleRate = 0;
        short channelCount = 0;
        short bitsPerSample = 0;
        short blockAlign = 0;
        int dataOffset = -1;
        int dataLength = 0;

        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> header = bytes.AsSpan(offset, 8);
            string chunkId = System.Text.Encoding.ASCII.GetString(header[..4]);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            int chunkDataOffset = offset + 8;
            if (chunkDataOffset + chunkSize > bytes.Length)
            {
                break;
            }

            if (string.Equals(chunkId, "fmt ", StringComparison.Ordinal))
            {
                ReadOnlySpan<byte> fmt = bytes.AsSpan(chunkDataOffset, chunkSize);
                short audioFormat = BinaryPrimitives.ReadInt16LittleEndian(fmt[..2]);
                channelCount = BinaryPrimitives.ReadInt16LittleEndian(fmt[2..4]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..8]);
                blockAlign = BinaryPrimitives.ReadInt16LittleEndian(fmt[12..14]);
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(fmt[14..16]);
                if (audioFormat != 1 || bitsPerSample != 16)
                {
                    throw new InvalidOperationException("Reference clip extraction currently supports PCM16 wave files only.");
                }
            }
            else if (string.Equals(chunkId, "data", StringComparison.Ordinal))
            {
                dataOffset = chunkDataOffset;
                dataLength = chunkSize;
                break;
            }

            offset = chunkDataOffset + chunkSize + (chunkSize % 2);
        }

        if (dataOffset < 0 || sampleRate <= 0 || channelCount <= 0 || blockAlign <= 0)
        {
            throw new InvalidOperationException("Wave metadata could not be parsed.");
        }

        return new WaveClipSource(
            dataOffset,
            dataLength,
            sampleRate,
            channelCount,
            bitsPerSample,
            blockAlign,
            dataLength / blockAlign);
    }

    private static async Task WriteWaveAsync(
        Stream destination,
        int sampleRate,
        short channelCount,
        short bitsPerSample,
        byte[] data,
        CancellationToken cancellationToken)
    {
        int blockAlign = channelCount * (bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int riffSize = 36 + data.Length;
        byte[] header = new byte[44];

        "RIFF"u8.CopyTo(header.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), riffSize);
        "WAVE"u8.CopyTo(header.AsSpan(8, 4));
        "fmt "u8.CopyTo(header.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22, 2), channelCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(header.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40, 4), data.Length);

        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    private sealed record WaveClipSource(
        int DataOffset,
        int DataLength,
        int SampleRate,
        short ChannelCount,
        short BitsPerSample,
        short BlockAlign,
        long FrameCount);
}
