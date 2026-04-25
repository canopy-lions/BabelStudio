using System.Buffers.Binary;
using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class KokoroPcmConverterTests
{
    [Fact]
    public void EncodePcm16Wav_EmptySamples_ReturnsOnlyHeader()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([], sampleRate: 24000);

        Assert.Equal(44, wav.Length);
    }

    [Fact]
    public void EncodePcm16Wav_WritesRiffFourCC()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        Assert.Equal((byte)'R', wav[0]);
        Assert.Equal((byte)'I', wav[1]);
        Assert.Equal((byte)'F', wav[2]);
        Assert.Equal((byte)'F', wav[3]);
    }

    [Fact]
    public void EncodePcm16Wav_WritesWaveFourCC()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        Assert.Equal((byte)'W', wav[8]);
        Assert.Equal((byte)'A', wav[9]);
        Assert.Equal((byte)'V', wav[10]);
        Assert.Equal((byte)'E', wav[11]);
    }

    [Fact]
    public void EncodePcm16Wav_WritesFmtChunkMarker()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        Assert.Equal((byte)'f', wav[12]);
        Assert.Equal((byte)'m', wav[13]);
        Assert.Equal((byte)'t', wav[14]);
        Assert.Equal((byte)' ', wav[15]);
    }

    [Fact]
    public void EncodePcm16Wav_WritesPcmFormatCode()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        ushort format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(20, 2));
        Assert.Equal(1, format); // PCM = 1
    }

    [Fact]
    public void EncodePcm16Wav_WritesMono()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(22, 2));
        Assert.Equal(1, channels);
    }

    [Fact]
    public void EncodePcm16Wav_WritesSampleRate()
    {
        const int sampleRate = 24000;
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate);

        int writtenRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24, 4));
        Assert.Equal(sampleRate, writtenRate);
    }

    [Fact]
    public void EncodePcm16Wav_WritesBitsPerSample16()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        ushort bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(34, 2));
        Assert.Equal(16, bitsPerSample);
    }

    [Fact]
    public void EncodePcm16Wav_WritesDataChunkMarker()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate: 24000);

        Assert.Equal((byte)'d', wav[36]);
        Assert.Equal((byte)'a', wav[37]);
        Assert.Equal((byte)'t', wav[38]);
        Assert.Equal((byte)'a', wav[39]);
    }

    [Fact]
    public void EncodePcm16Wav_TotalLengthIsHeaderPlusTwoBytePerSample()
    {
        float[] samples = new float[100];
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        Assert.Equal(44 + 100 * 2, wav.Length);
    }

    [Fact]
    public void EncodePcm16Wav_WritesCorrectRiffChunkSize()
    {
        float[] samples = new float[100];
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        // RIFF chunk size = 36 + dataBytes = 36 + (100 * 2) = 236
        int riffSize = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4, 4));
        Assert.Equal(36 + 100 * 2, riffSize);
    }

    [Fact]
    public void EncodePcm16Wav_SilentSamples_WriteZeroPcmData()
    {
        float[] samples = new float[4]; // all zeros
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        for (int i = 44; i < wav.Length; i++)
        {
            Assert.Equal(0, wav[i]);
        }
    }

    [Fact]
    public void EncodePcm16Wav_MaxPositiveSample_ClampedToInt16Max()
    {
        float[] samples = [2.0f]; // above 1.0, should clamp
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        short pcm = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44, 2));
        Assert.Equal(short.MaxValue, pcm);
    }

    [Fact]
    public void EncodePcm16Wav_MaxNegativeSample_ClampedToInt16Min()
    {
        float[] samples = [-2.0f]; // below -1.0, should clamp
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        short pcm = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44, 2));
        Assert.Equal(short.MinValue, pcm);
    }

    [Fact]
    public void EncodePcm16Wav_PositiveFullScale_MapsToNear32767()
    {
        float[] samples = [1.0f];
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        short pcm = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44, 2));
        Assert.Equal(32767, pcm); // 1.0f * 32767f = 32767
    }

    [Fact]
    public void EncodePcm16Wav_NegativeFullScale_MapsToMinus32767()
    {
        float[] samples = [-1.0f];
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        short pcm = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44, 2));
        Assert.Equal(-32767, pcm); // -1.0f * 32767f = -32767
    }

    [Fact]
    public void EncodePcm16Wav_WritesCorrectByteRateForMono16Bit()
    {
        const int sampleRate = 24000;
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([0f], sampleRate);

        // byteRate = sampleRate * channels * bitsPerSample / 8 = 24000 * 1 * 16 / 8 = 48000
        int byteRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28, 4));
        Assert.Equal(48000, byteRate);
    }

    [Fact]
    public void EncodePcm16Wav_WritesCorrectDataChunkSize()
    {
        float[] samples = new float[50];
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24000);

        int dataSize = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4));
        Assert.Equal(50 * 2, dataSize); // 50 samples * 2 bytes each = 100
    }
}