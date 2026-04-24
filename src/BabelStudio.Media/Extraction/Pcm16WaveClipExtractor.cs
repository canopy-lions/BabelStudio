using System.Buffers.Binary;
using BabelStudio.Application.Contracts;

namespace BabelStudio.Media.Extraction;

public sealed class Pcm16WaveClipExtractor : IAudioClipExtractor
{
    private const int MAX_CHUNK_SIZE = 1_000_000_000;

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

        WaveClipSource source;
        byte[] clipData;
        long startFrame;
        long endFrame;
        await using (FileStream sourceStream = new FileStream(sourceWavePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
        {
            byte[] headerBuffer = new byte[4096];
            int headerBytesRead = await sourceStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken).ConfigureAwait(false);
            if (headerBytesRead < 44)
            {
                throw new InvalidOperationException("Source wave file is too small to contain a valid header.");
            }

            source = ParseWave(headerBuffer.AsSpan(0, headerBytesRead));

            int bytesPerFrame = source.BlockAlign;
            startFrame = Math.Max(0L, (long)Math.Floor(startSeconds * source.SampleRate));
            endFrame = Math.Min(source.FrameCount, (long)Math.Ceiling(endSeconds * source.SampleRate));
            if (endFrame <= startFrame)
            {
                throw new InvalidOperationException("Clip range does not contain any audio frames.");
            }

            int clipDataLength = checked((int)((endFrame - startFrame) * bytesPerFrame));
            long dataOffset = checked(source.DataOffset + (startFrame * bytesPerFrame));
            clipData = new byte[clipDataLength];

            sourceStream.Seek(dataOffset, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < clipDataLength)
            {
                int bytesRead = await sourceStream.ReadAsync(clipData, totalRead, clipDataLength - totalRead, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new InvalidOperationException("Unexpected end of wave file while reading clip data.");
                }

                totalRead += bytesRead;
            }
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await WriteWaveAsync(destination, source.SampleRate, source.ChannelCount, source.BitsPerSample, clipData, cancellationToken).ConfigureAwait(false);

        return new AudioClipExtractionResult(
            destinationPath,
            (endFrame - startFrame) / (double)source.SampleRate,
            source.SampleRate,
            source.ChannelCount);
    }

    private static WaveClipSource ParseWave(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 44 ||
            !bytes.Slice(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
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
            ReadOnlySpan<byte> header = bytes.Slice(offset, 8);
            string chunkId = System.Text.Encoding.ASCII.GetString(header[..4]);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            int chunkDataOffset = offset + 8;

            if (chunkSize < 0 || chunkSize > MAX_CHUNK_SIZE)
            {
                throw new InvalidOperationException("Wave metadata could not be parsed.");
            }

            if (string.Equals(chunkId, "data", StringComparison.Ordinal))
            {
                dataOffset = chunkDataOffset;
                dataLength = chunkSize;
                break;
            }

            long nextOffset = (long)chunkDataOffset + chunkSize + (chunkSize % 2);
            if (nextOffset > bytes.Length)
            {
                if (!string.Equals(chunkId, "data", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Wave metadata could not be parsed.");
                }
                break;
            }

            if (string.Equals(chunkId, "fmt ", StringComparison.Ordinal))
            {
                if (chunkSize < 16)
                {
                    throw new InvalidOperationException("Wave metadata could not be parsed.");
                }

                ReadOnlySpan<byte> fmt = bytes.Slice(chunkDataOffset, chunkSize);
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

            offset = (int)nextOffset;
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
