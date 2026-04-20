using System.Text.Json;

namespace BabelStudio.Benchmarks.Reports;

internal static class BenchmarkReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        BenchmarkReport report,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, report, SerializerOptions, cancellationToken);
    }
}
