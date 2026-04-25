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
                "Language code may only contain letters, digits, underscores, and hyphens.",
                nameof(languageCode));
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add(languageCode);
        psi.ArgumentList.Add("--ipa=3");
        psi.ArgumentList.Add("-q");

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start espeak-ng at '{executablePath}'.");

        // Start reading stdout asynchronously before writing to stdin to avoid potential pipe deadlock.
        // If the process fills its stdout buffer before we start reading, it would block on write.
        Task<string> readTask = process.StandardOutput.ReadToEndAsync();

        process.StandardInput.Write(text.Trim());
        process.StandardInput.Close();

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
