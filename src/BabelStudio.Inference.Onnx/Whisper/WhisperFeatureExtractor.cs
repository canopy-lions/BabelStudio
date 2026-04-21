using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BabelStudio.Inference.Onnx.Whisper;

internal sealed class WhisperFeatureExtractor
{
    private const int SampleRate = 16000;
    private const int FftSize = 400;
    private const int HopLength = 160;
    private const int MelBins = 80;
    private const int MaxSamples = 480000;
    private const int MaxFrames = 3000;
    private const int FrequencyBins = 1 + (FftSize / 2);
    private readonly float[] hannWindow = BuildPeriodicHannWindow(FftSize);
    private readonly float[,] melFilters = BuildMelFilterBank();

    public DenseTensor<float> Extract(float[] inputSamples)
    {
        ArgumentNullException.ThrowIfNull(inputSamples);

        float[] paddedOrTrimmed = PadOrTrim(inputSamples, MaxSamples);
        float[] paddedForStft = ReflectPad(paddedOrTrimmed, FftSize / 2);
        float[,] powerSpectrum = ComputePowerSpectrum(paddedForStft);
        float[,] melSpectrum = ApplyMelFilters(powerSpectrum);
        NormalizeLogMel(melSpectrum);

        var data = new float[MelBins * MaxFrames];
        for (int melIndex = 0; melIndex < MelBins; melIndex++)
        {
            for (int frameIndex = 0; frameIndex < MaxFrames; frameIndex++)
            {
                data[(melIndex * MaxFrames) + frameIndex] = melSpectrum[melIndex, frameIndex];
            }
        }

        return new DenseTensor<float>(data, [1, MelBins, MaxFrames]);
    }

    public static float[] PrepareSamples(float[] inputSamples, int inputSampleRate) =>
        inputSampleRate == SampleRate
            ? inputSamples
            : Audio.AudioResampler.Resample(inputSamples, inputSampleRate, SampleRate);

    private float[,] ComputePowerSpectrum(float[] paddedSamples)
    {
        int frameCount = 1 + ((paddedSamples.Length - FftSize) / HopLength);
        var result = new float[FrequencyBins, MaxFrames];
        var spectrum = new Complex[FftSize];

        for (int frameIndex = 0; frameIndex < frameCount - 1 && frameIndex < MaxFrames; frameIndex++)
        {
            int sampleOffset = frameIndex * HopLength;
            Array.Clear(spectrum);
            for (int sampleIndex = 0; sampleIndex < FftSize; sampleIndex++)
            {
                double windowed = paddedSamples[sampleOffset + sampleIndex] * hannWindow[sampleIndex];
                spectrum[sampleIndex] = new Complex(windowed, 0);
            }

            Fourier.Forward(spectrum, FourierOptions.Matlab);

            for (int binIndex = 0; binIndex < FrequencyBins; binIndex++)
            {
                double magnitude = spectrum[binIndex].Magnitude;
                result[binIndex, frameIndex] = (float)(magnitude * magnitude);
            }
        }

        return result;
    }

    private float[,] ApplyMelFilters(float[,] powerSpectrum)
    {
        var melSpectrum = new float[MelBins, MaxFrames];
        for (int melIndex = 0; melIndex < MelBins; melIndex++)
        {
            for (int frameIndex = 0; frameIndex < MaxFrames; frameIndex++)
            {
                double sum = 0;
                for (int binIndex = 0; binIndex < FrequencyBins; binIndex++)
                {
                    sum += melFilters[melIndex, binIndex] * powerSpectrum[binIndex, frameIndex];
                }

                melSpectrum[melIndex, frameIndex] = (float)sum;
            }
        }

        return melSpectrum;
    }

    private static void NormalizeLogMel(float[,] melSpectrum)
    {
        float maxValue = float.NegativeInfinity;
        for (int melIndex = 0; melIndex < MelBins; melIndex++)
        {
            for (int frameIndex = 0; frameIndex < MaxFrames; frameIndex++)
            {
                float clamped = Math.Max(melSpectrum[melIndex, frameIndex], 1e-10f);
                float logValue = MathF.Log10(clamped);
                melSpectrum[melIndex, frameIndex] = logValue;
                if (logValue > maxValue)
                {
                    maxValue = logValue;
                }
            }
        }

        float minimumAllowed = maxValue - 8f;
        for (int melIndex = 0; melIndex < MelBins; melIndex++)
        {
            for (int frameIndex = 0; frameIndex < MaxFrames; frameIndex++)
            {
                float normalized = Math.Max(melSpectrum[melIndex, frameIndex], minimumAllowed);
                melSpectrum[melIndex, frameIndex] = (normalized + 4f) / 4f;
            }
        }
    }

    private static float[] PadOrTrim(float[] inputSamples, int targetLength)
    {
        if (inputSamples.Length == targetLength)
        {
            return inputSamples.ToArray();
        }

        var output = new float[targetLength];
        int copyLength = Math.Min(inputSamples.Length, targetLength);
        Array.Copy(inputSamples, output, copyLength);
        return output;
    }

    private static float[] ReflectPad(float[] samples, int padding)
    {
        if (samples.Length == 0 || padding <= 0)
        {
            return samples.ToArray();
        }

        var output = new float[samples.Length + (padding * 2)];
        for (int index = 0; index < padding; index++)
        {
            output[index] = samples[Math.Min(padding - index, samples.Length - 1)];
        }

        Array.Copy(samples, 0, output, padding, samples.Length);

        for (int index = 0; index < padding; index++)
        {
            int sourceIndex = Math.Max(samples.Length - 2 - index, 0);
            output[padding + samples.Length + index] = samples[sourceIndex];
        }

        return output;
    }

    private static float[] BuildPeriodicHannWindow(int length)
    {
        var window = new float[length];
        for (int index = 0; index < length; index++)
        {
            window[index] = (float)(0.5 - (0.5 * Math.Cos((2 * Math.PI * index) / length)));
        }

        return window;
    }

    private static float[,] BuildMelFilterBank()
    {
        var filters = new float[MelBins, FrequencyBins];
        double[] fftFrequencies = Enumerable.Range(0, FrequencyBins)
            .Select(index => index * (SampleRate / 2d) / (FrequencyBins - 1))
            .ToArray();

        double melMin = HertzToMel(0);
        double melMax = HertzToMel(SampleRate / 2d);
        double[] melPoints = Enumerable.Range(0, MelBins + 2)
            .Select(index => melMin + ((melMax - melMin) * index / (MelBins + 1d)))
            .ToArray();
        double[] hzPoints = melPoints.Select(MelToHertz).ToArray();

        for (int melIndex = 0; melIndex < MelBins; melIndex++)
        {
            double lower = hzPoints[melIndex];
            double center = hzPoints[melIndex + 1];
            double upper = hzPoints[melIndex + 2];
            double enorm = 2.0 / Math.Max(upper - lower, double.Epsilon);

            for (int binIndex = 0; binIndex < FrequencyBins; binIndex++)
            {
                double frequency = fftFrequencies[binIndex];
                double lowerSlope = (frequency - lower) / Math.Max(center - lower, double.Epsilon);
                double upperSlope = (upper - frequency) / Math.Max(upper - center, double.Epsilon);
                double weight = Math.Max(0, Math.Min(lowerSlope, upperSlope));
                filters[melIndex, binIndex] = (float)(weight * enorm);
            }
        }

        return filters;
    }

    private static double HertzToMel(double frequencyHertz)
    {
        const double fSp = 200d / 3d;
        const double minLogHertz = 1000d;
        double minLogMel = minLogHertz / fSp;
        double logStep = Math.Log(6.4) / 27d;

        return frequencyHertz >= minLogHertz
            ? minLogMel + (Math.Log(frequencyHertz / minLogHertz) / logStep)
            : frequencyHertz / fSp;
    }

    private static double MelToHertz(double mel)
    {
        const double fSp = 200d / 3d;
        const double minLogHertz = 1000d;
        double minLogMel = minLogHertz / fSp;
        double logStep = Math.Log(6.4) / 27d;

        return mel >= minLogMel
            ? minLogHertz * Math.Exp(logStep * (mel - minLogMel))
            : mel * fSp;
    }
}