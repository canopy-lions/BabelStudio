using System.Text.Json;
using System.Text.Json.Serialization;
using BabelStudio.Domain;

namespace BabelStudio.Benchmarks;

public static class BenchmarkReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static async Task WriteAsync(
        BenchmarkReport report,
        ReportFormat format,
        CancellationToken cancellationToken)
    {
        if (format is ReportFormat.Console)
        {
            return;
        }

        var directory = Path.GetDirectoryName(report.ReportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            report.ReportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, report, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAsync(
        BenchmarkBatchReport report,
        ReportFormat format,
        CancellationToken cancellationToken)
    {
        if (format is ReportFormat.Console)
        {
            return;
        }

        var directory = Path.GetDirectoryName(report.ReportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            report.ReportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, report, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
