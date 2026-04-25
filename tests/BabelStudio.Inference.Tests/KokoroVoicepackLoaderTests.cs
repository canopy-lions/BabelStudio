using System.Buffers.Binary;
using System.Runtime.InteropServices;
using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class KokoroVoicepackLoaderTests : IDisposable
{
    private const int StyleVectorSize = 256;
    private readonly string tempDir;

    public KokoroVoicepackLoaderTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"kokoro-voicepack-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Creates a .bin file with <paramref name="rowCount"/> rows of 256 floats.
    /// Each row i is filled with the float value i+1.
    /// </summary>
    private string CreateFakeBin(string filename, int rowCount)
    {
        string path = Path.Combine(tempDir, filename);
        float[] data = new float[rowCount * StyleVectorSize];
        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < StyleVectorSize; col++)
            {
                data[row * StyleVectorSize + col] = row + 1.0f;
            }
        }

        byte[] bytes = new byte[data.Length * sizeof(float)];
        MemoryMarshal.AsBytes(data.AsSpan()).CopyTo(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void LoadStyleVector_ReadsCorrectRow_ForTokenCount0()
    {
        string binPath = CreateFakeBin("voice.bin", rowCount: 10);

        float[] vector = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 0);

        Assert.Equal(StyleVectorSize, vector.Length);
        Assert.All(vector, v => Assert.Equal(1.0f, v)); // row 0 is filled with 1.0f
    }

    [Fact]
    public void LoadStyleVector_ReadsCorrectRow_ForTokenCount5()
    {
        string binPath = CreateFakeBin("voice.bin", rowCount: 10);

        float[] vector = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 5);

        // row 5 is filled with 6.0f (row+1)
        Assert.All(vector, v => Assert.Equal(6.0f, v));
    }

    [Fact]
    public void LoadStyleVector_ReadsCorrectRow_ForLastRow()
    {
        string binPath = CreateFakeBin("voice.bin", rowCount: 5);

        float[] vector = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 4);

        // row 4 is filled with 5.0f
        Assert.All(vector, v => Assert.Equal(5.0f, v));
    }

    [Fact]
    public void LoadStyleVector_ReturnsVectorOf256Floats()
    {
        string binPath = CreateFakeBin("voice.bin", rowCount: 3);

        float[] vector = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 0);

        Assert.Equal(StyleVectorSize, vector.Length);
    }

    [Fact]
    public void LoadStyleVector_ThrowsInvalidOperationException_WhenFileTooSmall()
    {
        // Create a file that has only 1 row (offset 1 requires 2 rows)
        string binPath = CreateFakeBin("tiny.bin", rowCount: 1);

        // Requesting tokenCount=1 requires offset 1*256*4 + 256*4 = 512*4 bytes = 2 rows
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 1));

        Assert.Contains("style rows", ex.Message);
    }

    [Fact]
    public void LoadStyleVector_ErrorMessage_IncludesFilenameAndTokenCount()
    {
        string binPath = CreateFakeBin("af_heart.bin", rowCount: 1);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 5));

        Assert.Contains("af_heart.bin", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void LoadStyleVector_EmptyFile_ThrowsForAnyTokenCount()
    {
        string path = Path.Combine(tempDir, "empty.bin");
        File.WriteAllBytes(path, []);

        Assert.Throws<InvalidOperationException>(
            () => KokoroVoicepackLoader.LoadStyleVector(path, tokenCount: 0));
    }

    [Fact]
    public void LoadStyleVector_DoesNotReadBeyondRequestedRow()
    {
        // File has exactly 3 rows; reading row 2 (last) should succeed
        string binPath = CreateFakeBin("voice.bin", rowCount: 3);

        float[] vector = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 2);

        Assert.Equal(StyleVectorSize, vector.Length);
        Assert.All(vector, v => Assert.Equal(3.0f, v)); // row 2 = value 3.0f
    }

    [Fact]
    public void LoadStyleVector_ReadsBytesInLittleEndianOrder()
    {
        // Write a single known float value in little-endian and verify round-trip
        float expected = 3.14159f;
        string path = Path.Combine(tempDir, "known.bin");
        byte[] bytes = new byte[StyleVectorSize * sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, expected);
        File.WriteAllBytes(path, bytes);

        float[] vector = KokoroVoicepackLoader.LoadStyleVector(path, tokenCount: 0);

        Assert.Equal(expected, vector[0], precision: 4);
    }
}
