using System.Diagnostics;
using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.Inference.Onnx.Kokoro;

public sealed class EspeakNgPhonemizer : IGraphemeToPhoneme
{
    private readonly string executablePath;

    public EspeakNgPhonemizer(string executablePath = "espeak-ng")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = executablePath;
    }

    public string Phonemize(string text, string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"-v {languageCode} --ipa=3 -q",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start espeak-ng at '{executablePath}'.");

        process.StandardInput.Write(text.Trim());
        process.StandardInput.Close();
        string output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"espeak-ng did not complete within 10 seconds for text of {text.Length} chars.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"espeak-ng exited with code {process.ExitCode}.");
        }

        return NormalizeIpaOutput(output);
    }

    private static string NormalizeIpaOutput(string raw) =>
        raw.Replace("\r\n", " ").Replace('\n', ' ').Replace('_', ' ').Trim();
}
