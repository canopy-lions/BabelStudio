namespace BabelStudio.Benchmarks;

internal sealed record BenchmarkOptions(
    string ModelPath,
    string OutputPath,
    string? FixturePath,
    BenchmarkProviderPreference ProviderPreference,
    int DeviceId,
    int RunCount,
    bool ShowHelp)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        TextWriter errorWriter,
        out BenchmarkOptions options)
    {
        string? modelPath = null;
        string outputPath = Path.Combine(Environment.CurrentDirectory, "benchmark-report.json");
        string? fixturePath = null;
        var providerPreference = BenchmarkProviderPreference.Cpu;
        var deviceId = 0;
        var runCount = 10;
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
                    if (!TryReadValue(args, ref index, arg, errorWriter, out string outputPathValue))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    outputPath = outputPathValue;
                    break;

                case "--fixture":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out fixturePath))
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

                case "--device-id":
                    if (!TryReadValue(args, ref index, arg, errorWriter, out string deviceIdText))
                    {
                        options = DefaultWithHelp();
                        return false;
                    }

                    if (!int.TryParse(deviceIdText, out deviceId) || deviceId < 0)
                    {
                        errorWriter.WriteLine($"Invalid device id '{deviceIdText}'.");
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

                default:
                    errorWriter.WriteLine($"Unknown argument '{arg}'.");
                    options = DefaultWithHelp();
                    return false;
            }
        }

        if (showHelp)
        {
            options = new BenchmarkOptions(string.Empty, outputPath, fixturePath, providerPreference, deviceId, runCount, ShowHelp: true);
            return true;
        }

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            errorWriter.WriteLine("Missing required argument --model <path>.");
            options = DefaultWithHelp();
            return false;
        }

        options = new BenchmarkOptions(
            Path.GetFullPath(modelPath),
            Path.GetFullPath(outputPath),
            fixturePath is null ? null : Path.GetFullPath(fixturePath),
            providerPreference,
            deviceId,
            runCount,
            ShowHelp: false);

        return true;
    }

    private static BenchmarkOptions DefaultWithHelp() =>
        new(string.Empty, Path.Combine(Environment.CurrentDirectory, "benchmark-report.json"), null, BenchmarkProviderPreference.Cpu, 0, 10, ShowHelp: true);

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

internal enum BenchmarkProviderPreference
{
    Auto,
    Cpu,
    Dml
}
