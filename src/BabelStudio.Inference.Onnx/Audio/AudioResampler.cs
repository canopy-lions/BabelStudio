namespace BabelStudio.Inference.Onnx.Audio;

internal static class AudioResampler
{
    public static float[] Resample(float[] input, int inputSampleRate, int outputSampleRate)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (inputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSampleRate), "Sample rate must be positive.");
        }

        if (outputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate), "Sample rate must be positive.");
        }

        if (input.Length == 0 || inputSampleRate == outputSampleRate)
        {
            return input.ToArray();
        }

        int outputLength = Math.Max(1, (int)Math.Round(input.Length * (double)outputSampleRate / inputSampleRate));
        var output = new float[outputLength];
        double ratio = (double)inputSampleRate / outputSampleRate;

        for (int index = 0; index < outputLength; index++)
        {
            double sourcePosition = index * ratio;
            int leftIndex = (int)Math.Floor(sourcePosition);
            int rightIndex = Math.Min(leftIndex + 1, input.Length - 1);
            double fraction = sourcePosition - leftIndex;

            float left = input[Math.Min(leftIndex, input.Length - 1)];
            float right = input[rightIndex];
            output[index] = (float)(left + ((right - left) * fraction));
        }

        return output;
    }
}
