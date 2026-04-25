using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.Inference.Onnx.Kokoro;

public sealed partial class EspeakNgPhonemizer : IGraphemeToPhoneme
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

        if (!LanguageCodePattern().IsMatch(languageCode))
        {
            throw new ArgumentException(
                $"Invalid language code: '{languageCode}'. Expected pattern [A-Za-z0-9_-]+.",
                nameof(languageCode));
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"-v {languageCode} --ipa=3 -q",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start espeak-ng at '{executablePath}'.");

        process.StandardInput.Write(text.Trim());
        process.StandardInput.Close();

        Task<string> readTask = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"espeak-ng did not complete within 10 seconds for text of {text.Length} chars.");
        }

        string output = readTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"espeak-ng exited with code {process.ExitCode}.");
        }

        return NormalizeIpaOutput(output);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex LanguageCodePattern();

    private static string NormalizeIpaOutput(string raw) =>
        raw.Replace("\r\n", " ").Replace('\n', ' ').Replace('_', ' ').Trim();
}
