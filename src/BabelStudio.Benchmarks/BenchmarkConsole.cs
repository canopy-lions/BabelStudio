using BabelStudio.Benchmarks.Reports;

namespace BabelStudio.Benchmarks;

internal static class BenchmarkConsole
{
    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("BabelStudio.Benchmarks");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  BabelStudio.Benchmarks --model <path> [--output <path>] [--fixture <path>] [--provider cpu|auto|dml] [--device-id <n>] [--runs <n>]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --model <path>      Required ONNX model path.");
        writer.WriteLine("  --output <path>     Output report path. Defaults to benchmark-report.json in the current directory.");
        writer.WriteLine("  --fixture <path>    Optional JSON fixture file for model inputs.");
        writer.WriteLine("  --provider <name>   Provider preference: cpu, auto, or dml. Defaults to cpu.");
        writer.WriteLine("  --device-id <n>     DirectML device id. Defaults to 0.");
        writer.WriteLine("  --runs <n>          Number of measured runs after warmup. Defaults to 10.");
        writer.WriteLine("  --help              Show this help.");
        writer.WriteLine();
        writer.WriteLine("Fixture format:");
        writer.WriteLine("""
{
  "inputs": [
    {
      "name": "input",
      "dimensions": [1, 3, 224, 224],
      "values": [1.0, 1.0, 1.0]
    }
  ]
}
""");
    }

    public static void WriteSummary(BenchmarkReport report, TextWriter writer)
    {
        writer.WriteLine($"Model: {report.ModelPath}");
        writer.WriteLine($"Provider: {report.SelectedProvider}");
        writer.WriteLine($"Available providers: {string.Join(", ", report.AvailableProviders)}");
        writer.WriteLine($"Measured runs: {report.MeasuredRunCount}");
        writer.WriteLine($"Cold load: {report.ColdLoadMilliseconds:F2} ms");
        writer.WriteLine($"Warmup: {report.WarmupMilliseconds:F2} ms");
        writer.WriteLine($"Warm latency avg/min/max: {report.WarmLatencyAverageMilliseconds:F2} / {report.WarmLatencyMinimumMilliseconds:F2} / {report.WarmLatencyMaximumMilliseconds:F2} ms");

        if (report.Warnings.Count > 0)
        {
            writer.WriteLine("Warnings:");
            foreach (var warning in report.Warnings)
            {
                writer.WriteLine($"  - {warning}");
            }
        }
    }
}
