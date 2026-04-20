namespace BabelStudio.Media.Process;

internal sealed class FfmpegToolResolver
{
    private readonly string? explicitFfmpegPath;
    private readonly string? explicitFfprobePath;

    public FfmpegToolResolver(string? explicitFfmpegPath = null, string? explicitFfprobePath = null)
    {
        this.explicitFfmpegPath = explicitFfmpegPath;
        this.explicitFfprobePath = explicitFfprobePath;
    }

    public string ResolveFfmpegPath() =>
        ResolveExecutable(
            explicitFfmpegPath,
            "BABELSTUDIO_FFMPEG_PATH",
            ["ffmpeg.exe", "ffmpeg"]);

    public string ResolveFfprobePath()
    {
        string ffmpegPath = ResolveFfmpegPath();
        string preferredFfprobeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        string? ffprobePath = TryResolveExecutable(
            explicitFfprobePath,
            "BABELSTUDIO_FFPROBE_PATH",
            ["ffprobe.exe", "ffprobe"]);

        if (!string.IsNullOrWhiteSpace(ffprobePath))
        {
            return ffprobePath;
        }

        string ffmpegDirectory = Path.GetDirectoryName(ffmpegPath)!;
        foreach (string candidate in new[]
                 {
                     Path.Combine(ffmpegDirectory, "ffprobe.exe"),
                     Path.Combine(ffmpegDirectory, "ffprobe"),
                     Path.Combine(Directory.GetParent(ffmpegDirectory)?.FullName ?? ffmpegDirectory, "ffprobe.exe"),
                     Path.Combine(Directory.GetParent(ffmpegDirectory)?.FullName ?? ffmpegDirectory, "ffprobe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string[] commonRoots =
        [
            ffmpegDirectory,
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];

        foreach (string root in commonRoots
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string alternateFfprobeName = preferredFfprobeName == "ffprobe.exe" ? "ffprobe" : "ffprobe.exe";
            foreach (string candidateName in new[] { preferredFfprobeName, alternateFfprobeName })
            {
                string? discovered = SearchRecursively(root, candidateName);
                if (!string.IsNullOrWhiteSpace(discovered))
                {
                    return discovered;
                }
            }
        }

        throw new InvalidOperationException(
            "Unable to locate ffprobe. Configure BABELSTUDIO_FFPROBE_PATH or install FFmpeg with ffprobe.");
    }

    private static string ResolveExecutable(
        string? explicitPath,
        string environmentVariable,
        IReadOnlyList<string> fallbacks)
    {
        string? resolved = TryResolveExecutable(explicitPath, environmentVariable, fallbacks);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(
            $"Unable to locate '{fallbacks[0]}'. Configure {environmentVariable} or install FFmpeg.");
    }

    private static string? TryResolveExecutable(
        string? explicitPath,
        string environmentVariable,
        IReadOnlyList<string> fallbacks)
    {
        foreach (string? candidate in new[]
                 {
                     explicitPath,
                     Environment.GetEnvironmentVariable(environmentVariable)
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        foreach (string fallback in fallbacks)
        {
            string? onPath = FindOnPath(fallback);
            if (!string.IsNullOrWhiteSpace(onPath))
            {
                return onPath;
            }
        }

        return null;
    }

    private static string? FindOnPath(string executableName)
    {
        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnvironment))
        {
            return null;
        }

        foreach (string pathSegment in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(pathSegment.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? SearchRecursively(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
            {
                return file;
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }
}
