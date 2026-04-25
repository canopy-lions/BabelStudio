namespace BabelStudio.Inference.Onnx.Kokoro;

public static class EspeakNgPathResolver
{
    private const string ExecutableName = "espeak-ng.exe";

    public static string Resolve(string? explicitPath = null, string? baseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath.Trim();
        }

        string appBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        foreach (string candidate in GetBundledCandidates(appBaseDirectory))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "espeak-ng";
    }

    public static IReadOnlyList<string> GetBundledCandidates(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string root = Path.GetFullPath(baseDirectory);
        return
        [
            Path.Combine(root, "tools", "espeak-ng", ExecutableName),
            Path.Combine(root, "runtimes", "win-x64", "native", "espeak-ng", ExecutableName),
            Path.Combine(root, "runtimes", "win-x64", "native", ExecutableName),
            Path.Combine(root, "espeak-ng", ExecutableName)
        ];
    }
}
