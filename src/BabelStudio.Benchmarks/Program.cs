using BabelStudio.Benchmarks;
using BabelStudio.Benchmarks.Reports;
using BabelStudio.Benchmarks.Scenarios;

return await ProgramEntryPoint.RunAsync(args);

internal static class ProgramEntryPoint
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!BenchmarkOptions.TryParse(args, Console.Out, out var options))
        {
            return 1;
        }

        if (options.ShowHelp)
        {
            BenchmarkConsole.WriteUsage(Console.Out);
            return 0;
        }

        try
        {
            var scenario = new OnnxModelBenchmarkScenario();
            BenchmarkReport report = await scenario.RunAsync(options, CancellationToken.None);

            BenchmarkConsole.WriteSummary(report, Console.Out);
            await BenchmarkReportWriter.WriteAsync(report, options.OutputPath, CancellationToken.None);

            Console.WriteLine($"Report written to: {options.OutputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
