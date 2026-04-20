using System.Diagnostics;

namespace BabelStudio.Media.Tests;

internal static class MediaFixtureFactory
{
    public static async Task<string> CreateSampleVideoAsync(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        string outputPath = Path.Combine(directoryPath, "sample-input.mp4");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in new[]
                 {
                     "-y",
                     "-hide_banner",
                     "-loglevel", "error",
                     "-f", "lavfi",
                     "-i", "sine=frequency=880:duration=1.25",
                     "-f", "lavfi",
                     "-i", "color=color=blue:size=64x64:duration=1.25:rate=24",
                     "-shortest",
                     "-c:v", "libx264",
                     "-pix_fmt", "yuv420p",
                     "-c:a", "aac",
                     outputPath
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        Assert.True(process.ExitCode == 0, $"ffmpeg failed to create test media: {error}");
        return outputPath;
    }
}