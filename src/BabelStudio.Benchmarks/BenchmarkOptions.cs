using BabelStudio.Domain;

namespace BabelStudio.Benchmarks;

public sealed record BenchmarkOptions(
    string ModelPath,
    string OutputPath,
    BenchmarkProviderPreference ProviderPreference,
    int RunCount,
    string? Variant,
    bool AllVariants,
    ReportFormat ReportFormat,
    bool ShowHelp)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        TextWriter errorWriter,
        out BenchmarkOptions options)
    {
        string? modelPath = null;
        var outputPath = Path.Combine(Environment.CurrentDirectory, "benchmark-report.json");
        var providerPreference = BenchmarkProviderPreference.Cpu;
        var runCount = 5;
        string? variant = null;
        var allVariants = false;
        var reportFormat = ReportFormat.Both;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            string arg = args[index];

            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;

                case "--model":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out modelPath))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    break;

                case "--output":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out outputPath))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    break;

                case "--provider":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out string providerText))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    if (!Enum.TryParse(providerText, ignoreCase: true, out providerPreference))
                    {
                        errorWriter.WriteLine($"Unknown provider '{providerText}'. Expected auto, cpu, or dml.");
                        options = DefaultWithHelp();
                        return false;
                    }

                    break;

                case "--runs":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out string runCountText))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    if (!int.TryParse(runCountText, out runCount) || runCount <= 0)
                    {
                        errorWriter.WriteLine($"Invalid run count '{runCountText}'.");
                        options = DefaultWithHelp();
                        return false;
                    }

                    break;

                case "--variant":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out variant))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    variant = variant.Trim();
                    break;

                case "--all-variants":
                    allVariants = true;
                    break;

                case "--format":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out string formatText))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    if (!Enum.TryParse(formatText, ignoreCase: true, out reportFormat))
                    {
                        errorWriter.WriteLine($"Unknown format '{formatText}'. Expected console, json, or both.");
                        options = DefaultWithHelp();
                        return false;
                    }

                    break;

                default:
                    errorWriter.WriteLine($"Unknown argument '{arg}'.");
                    options = DefaultWithHelp();
                    return false;
            }
        }

        if (showHelp)
        {
            options = new BenchmarkOptions(string.Empty, outputPath, providerPreference, runCount, variant, allVariants, reportFormat, ShowHelp: true);
            return true;
        }

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            errorWriter.WriteLine("Missing required argument --model <path-or-scope>.");
            options = DefaultWithHelp();
            return false;
        }

        if (allVariants && !string.IsNullOrWhiteSpace(variant))
        {
            errorWriter.WriteLine("Cannot combine --variant with --all-variants.");
            options = DefaultWithHelp();
            return false;
        }

        options = new BenchmarkOptions(
            modelPath.Trim(),
            Path.GetFullPath(outputPath),
            providerPreference,
            runCount,
            string.IsNullOrWhiteSpace(variant) ? null : variant,
            allVariants,
            reportFormat,
            ShowHelp: false);

        return true;
    }

    private static BenchmarkOptions DefaultWithHelp() =>
        new(string.Empty, Path.Combine(Environment.CurrentDirectory, "benchmark-report.json"), BenchmarkProviderPreference.Cpu, 5, null, false, ReportFormat.Both, ShowHelp: true);

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        TextWriter errorWriter,
        out string value)
    {
        if (index + 1 >= args.Count)
        {
            errorWriter.WriteLine($"Missing value for {optionName}.");
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }
}

public enum ReportFormat
{
    Console,
    Json,
    Both
}
