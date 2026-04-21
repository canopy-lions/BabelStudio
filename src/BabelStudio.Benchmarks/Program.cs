using BabelStudio.Domain;
using BabelStudio.Inference;
using BabelStudio.Inference.Onnx;

namespace BabelStudio.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        int cancelSignalCount = 0;
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            if (Interlocked.Increment(ref cancelSignalCount) == 1)
            {
                eventArgs.Cancel = true;
                cancellationTokenSource.Cancel();
                return;
            }

            eventArgs.Cancel = false;
        };

        Console.CancelKeyPress += handler;
        try
        {
            return await RunAsync(
                args,
                Console.In,
                Console.Out,
                Console.Error,
                cancellationTokenSource.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!BenchmarkOptions.TryParse(args, error, out var options))
        {
            BenchmarkConsole.WriteUsage(error);
            return 1;
        }

        if (options.ShowHelp)
        {
            BenchmarkConsole.WriteUsage(output);
            return 0;
        }

        try
        {
            var runner = CreateRunner();
            var resolver = BenchmarkModelPathResolver.CreateDefault();
            var defaultsStore = BenchmarkSelectionDefaultsStore.LoadDefault();

            if (options.AllVariants)
            {
                return await RunAllVariantsAsync(options, resolver, runner, output, cancellationToken);
            }

            BenchmarkModelCandidate candidate = await ResolveSingleCandidateAsync(
                options,
                resolver,
                defaultsStore,
                input,
                output,
                cancellationToken);

            var request = new BenchmarkRequest(
                candidate.ModelPath,
                options.OutputPath,
                options.ProviderPreference,
                options.RunCount);

            BenchmarkReport report = await runner.RunAsync(request, cancellationToken);
            report = AddResolutionNote(report, candidate);
            await BenchmarkReportWriter.WriteAsync(report, options.ReportFormat, cancellationToken);

            if (options.ReportFormat is ReportFormat.Console or ReportFormat.Both)
            {
                BenchmarkConsole.WriteSummary(report, output);
            }

            if (options.ReportFormat is ReportFormat.Json or ReportFormat.Both)
            {
                output.WriteLine($"Report written to: {report.ReportPath}");
            }

            return report.Status is BenchmarkStatus.Failed ? 1 : 0;
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static IModelBenchmarkRunner CreateRunner() => new OnnxModelBenchmarkRunner();

    private static async Task<int> RunAllVariantsAsync(
        BenchmarkOptions options,
        BenchmarkModelPathResolver resolver,
        IModelBenchmarkRunner runner,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        BenchmarkModelResolutionResult discovery = resolver.Discover(options.ModelPath);
        if (!string.IsNullOrWhiteSpace(discovery.Error))
        {
            throw new FileNotFoundException(discovery.Error, options.ModelPath);
        }

        if (discovery.Candidates.Count == 0)
        {
            throw new FileNotFoundException("Model path or scope did not resolve to an ONNX model.", options.ModelPath);
        }

        var reports = new List<BenchmarkReport>(discovery.Candidates.Count);
        foreach (BenchmarkModelCandidate candidate in discovery.Candidates)
        {
            string reportPath = DeriveVariantReportPath(options.OutputPath, candidate);
            var request = new BenchmarkRequest(
                candidate.ModelPath,
                reportPath,
                options.ProviderPreference,
                options.RunCount);

            BenchmarkReport report = await runner.RunAsync(request, cancellationToken);
            report = AddResolutionNote(report, candidate);
            reports.Add(report);

            await BenchmarkReportWriter.WriteAsync(report, options.ReportFormat, cancellationToken);
        }

        var batchReport = new BenchmarkBatchReport(
            RequestedReference: options.ModelPath,
            ReportPath: options.OutputPath,
            Results: reports,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        await BenchmarkReportWriter.WriteAsync(batchReport, options.ReportFormat, cancellationToken);

        if (options.ReportFormat is ReportFormat.Console or ReportFormat.Both)
        {
            BenchmarkConsole.WriteBatchSummary(batchReport, output);
        }

        if (options.ReportFormat is ReportFormat.Json or ReportFormat.Both)
        {
            output.WriteLine($"Batch report written to: {batchReport.ReportPath}");
        }

        return reports.Any(report => report.Status is BenchmarkStatus.Failed) ? 1 : 0;
    }

    private static async Task<BenchmarkModelCandidate> ResolveSingleCandidateAsync(
        BenchmarkOptions options,
        BenchmarkModelPathResolver resolver,
        BenchmarkSelectionDefaultsStore defaultsStore,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Variant))
        {
            return resolver.ResolveSingle(options.ModelPath, options.Variant);
        }

        if (options.ModelPath.Contains('@', StringComparison.Ordinal))
        {
            return resolver.ResolveSingle(options.ModelPath);
        }

        BenchmarkModelResolutionResult discovery = resolver.Discover(options.ModelPath);
        if (!string.IsNullOrWhiteSpace(discovery.Error))
        {
            throw new FileNotFoundException(discovery.Error, options.ModelPath);
        }

        if (defaultsStore.TryGet(discovery.ScopeKey, out string? storedCandidateKey) &&
            !string.IsNullOrWhiteSpace(storedCandidateKey))
        {
            BenchmarkModelCandidate? storedCandidate = discovery.Candidates.FirstOrDefault(
                candidate => candidate.CandidateKey.Equals(storedCandidateKey, StringComparison.OrdinalIgnoreCase));

            if (storedCandidate is not null)
            {
                return storedCandidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(discovery.DefaultCandidateKey))
        {
            BenchmarkModelCandidate? defaultCandidate = discovery.Candidates.FirstOrDefault(
                candidate => candidate.CandidateKey.Equals(discovery.DefaultCandidateKey, StringComparison.OrdinalIgnoreCase));

            if (defaultCandidate is not null)
            {
                return defaultCandidate;
            }
        }

        if (discovery.Candidates.Count == 1)
        {
            return discovery.Candidates[0];
        }

        if (discovery.Candidates.Count == 0)
        {
            throw new FileNotFoundException("Model path or scope did not resolve to an ONNX model.", options.ModelPath);
        }

        BenchmarkModelCandidate selectedCandidate = await PromptForCandidateAsync(discovery, input, output, cancellationToken);
        defaultsStore.Set(discovery.ScopeKey, selectedCandidate.CandidateKey);
        await defaultsStore.SaveAsync(cancellationToken);
        return selectedCandidate;
    }

    private static async Task<BenchmarkModelCandidate> PromptForCandidateAsync(
        BenchmarkModelResolutionResult discovery,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await output.WriteLineAsync($"Multiple benchmarkable ONNX variants were found for '{discovery.RequestedReference}'.");
        for (int index = 0; index < discovery.Candidates.Count; index++)
        {
            BenchmarkModelCandidate candidate = discovery.Candidates[index];
            await output.WriteLineAsync($"{index + 1}. {candidate.DisplayName} -> {candidate.ModelPath}");
        }

        await output.WriteAsync("Choose the default variant number to remember for this machine: ");
        string? response = await input.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(response) ||
            !int.TryParse(response, out int selectedIndex) ||
            selectedIndex < 1 ||
            selectedIndex > discovery.Candidates.Count)
        {
            throw new InvalidOperationException("Ambiguous model selection requires a valid variant number.");
        }

        return discovery.Candidates[selectedIndex - 1];
    }

    private static BenchmarkReport AddResolutionNote(BenchmarkReport report, BenchmarkModelCandidate candidate)
    {
        if (report.Notes.Any(note => note.Equals(candidate.ResolutionNote, StringComparison.Ordinal)))
        {
            return report;
        }

        return report with
        {
            Notes = new[] { candidate.ResolutionNote }.Concat(report.Notes).ToArray()
        };
    }

    private static string DeriveVariantReportPath(string aggregateReportPath, BenchmarkModelCandidate candidate)
    {
        string directory = Path.GetDirectoryName(aggregateReportPath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(aggregateReportPath);
        string extension = Path.GetExtension(aggregateReportPath);
        string suffix = candidate.VariantAlias ?? Path.GetFileNameWithoutExtension(candidate.ModelPath);
        string sanitizedSuffix = SanitizeFileNameSegment(suffix);
        return Path.Combine(directory, $"{fileNameWithoutExtension}-{sanitizedSuffix}{extension}");
    }

    private static string SanitizeFileNameSegment(string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "variant" : sanitized;
    }
}
