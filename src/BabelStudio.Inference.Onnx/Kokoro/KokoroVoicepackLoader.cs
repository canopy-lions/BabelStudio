using System.Runtime.InteropServices;

namespace BabelStudio.Inference.Onnx.Kokoro;

internal static class KokoroVoicepackLoader
{
    private const int StyleVectorSize = 256;

    // tokenCount is the full input_ids sequence length (including BOS/EOS tokens).
    // The .bin file stores one 256-float style vector per token-count row (raw float32 LE).
    public static float[] LoadStyleVector(string binPath, int tokenCount)
    {
        long byteOffset = (long)tokenCount * StyleVectorSize * sizeof(float);
        long requiredBytes = byteOffset + StyleVectorSize * sizeof(float);

        using var stream = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < requiredBytes)
        {
            throw new InvalidOperationException(
                $"Voicepack '{Path.GetFileName(binPath)}' is too small for token count {tokenCount}. " +
                $"Need {requiredBytes} bytes, file has {stream.Length}.");
        }

        stream.Seek(byteOffset, SeekOrigin.Begin);
        byte[] buffer = new byte[StyleVectorSize * sizeof(float)];
        stream.ReadExactly(buffer);

        float[] styleVector = new float[StyleVectorSize];
        MemoryMarshal.Cast<byte, float>(buffer).CopyTo(styleVector);
        return styleVector;
    }
}
