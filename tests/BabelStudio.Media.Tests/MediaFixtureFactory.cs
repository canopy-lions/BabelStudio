using System.Diagnostics;

namespace BabelStudio.Media.Tests;

internal static class MediaFixtureFactory
{
    public static async Task<string> CreateSampleVideoAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
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

        System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'. Ensure ffmpeg is installed and available on PATH.");

        using (process)
        {
            try
            {
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);
                Assert.True(process.ExitCode == 0, $"ffmpeg failed to create test media: {error}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                throw;
            }
        }

        return outputPath;
    }
}
