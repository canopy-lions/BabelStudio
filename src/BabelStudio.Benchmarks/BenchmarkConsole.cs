using BabelStudio.Domain;

namespace BabelStudio.Benchmarks;

public static class BenchmarkConsole
{
    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("BabelStudio.Benchmarks");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  BabelStudio.Benchmarks ingest --project <path> [--name <project-name> --media <path> | --open] [--ffmpeg <path>] [--ffprobe <path>]");
        writer.WriteLine("  BabelStudio.Benchmarks ingest --help");
        writer.WriteLine("  BabelStudio.Benchmarks --help");
        writer.WriteLine("  BabelStudio.Benchmarks --model <path-or-scope> [--variant <name> | --all-variants] [--output <path>] [--provider cpu|auto|dml] [--runs <n>] [--format console|json|both]");
        writer.WriteLine();
        writer.WriteLine("Ingest command:");
        writer.WriteLine("  --project <path>    Required project root, typically ending in .babelstudio.");
        writer.WriteLine("  --name <name>       Required for create mode. Project display name.");
        writer.WriteLine("  --media <path>      Required for create mode. Source media file to ingest.");
        writer.WriteLine("  --open              Open an existing project and report source/artifact status.");
        writer.WriteLine("  --ffmpeg <path>     Optional explicit ffmpeg executable path.");
        writer.WriteLine("  --ffprobe <path>    Optional explicit ffprobe executable path.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --model <path-or-scope>  Required ONNX model path or scoped model reference under ./models.");
        writer.WriteLine("  --output <path>     Output report path. Defaults to benchmark-report.json in the current directory.");
        writer.WriteLine("  --provider <name>   Provider preference: cpu, auto, or dml. Defaults to cpu.");
        writer.WriteLine("  --runs <n>          Planned measured run count. Defaults to 5.");
        writer.WriteLine("  --variant <name>    Run a specific variant for the selected model reference.");
        writer.WriteLine("  --all-variants      Run every discovered benchmarkable variant and emit an aggregate report.");
        writer.WriteLine("  --format <name>     Output mode: console, json, or both. Defaults to both.");
        writer.WriteLine("  --help              Show this help.");
    }

    public static void WriteSummary(BenchmarkReport report, TextWriter writer)
    {
        writer.WriteLine($"Scenario: {report.Scenario}");
        writer.WriteLine($"Model: {report.ModelPath}");
        writer.WriteLine($"Status: {report.Status}");
        writer.WriteLine($"Supports execution: {report.SupportsExecution}");
        writer.WriteLine($"Requested provider: {report.RequestedProvider}");
        writer.WriteLine($"Selected provider: {report.SelectedProvider}");
        writer.WriteLine($"Requested runs: {report.RunCount}");
        writer.WriteLine($"Model size: {report.ModelSizeBytes} bytes");
        writer.WriteLine($"Cold load: {FormatMilliseconds(report.Measurements.ColdLoadMilliseconds)}");
        writer.WriteLine($"Warmup: {FormatMilliseconds(report.Measurements.WarmupMilliseconds)}");
        writer.WriteLine($"Warm latency avg/min/max: {FormatMilliseconds(report.Measurements.WarmLatencyAverageMilliseconds)} / {FormatMilliseconds(report.Measurements.WarmLatencyMinimumMilliseconds)} / {FormatMilliseconds(report.Measurements.WarmLatencyMaximumMilliseconds)}");
        writer.WriteLine($"Audio duration: {FormatSeconds(report.Measurements.AudioDurationSeconds)}");
        writer.WriteLine($"Real-time factor: {FormatFactor(report.Measurements.RealTimeFactorAverage)}");
        writer.WriteLine($"Report path: {report.ReportPath}");

        if (!string.IsNullOrWhiteSpace(report.FailureReason))
        {
            writer.WriteLine($"Failure reason: {report.FailureReason}");
        }

        if (report.Notes.Count > 0)
        {
            writer.WriteLine("Notes:");
            foreach (var note in report.Notes)
            {
                writer.WriteLine($"  - {note}");
            }
        }
    }

    public static void WriteBatchSummary(BenchmarkBatchReport report, TextWriter writer)
    {
        writer.WriteLine($"Batch reference: {report.RequestedReference}");
        writer.WriteLine($"Batch report path: {report.ReportPath}");
        writer.WriteLine($"Batch results: {report.Results.Count}");

        foreach (BenchmarkReport result in report.Results)
        {
            writer.WriteLine();
            WriteSummary(result, writer);
        }
    }

    private static string FormatMilliseconds(double? value) =>
        value is null ? "n/a" : $"{value.Value:F2} ms";

    private static string FormatSeconds(double? value) =>
        value is null ? "n/a" : $"{value.Value:F3} s";

    private static string FormatFactor(double? value) =>
        value is null ? "n/a" : $"{value.Value:F3}x";
}
