namespace BabelStudio.Tools;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        Console.CancelKeyPress += handler;
        try
        {
            return await RunAsync(
                args,
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
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string[] effectiveArgs;
        if (args.Length == 0)
        {
            effectiveArgs = args;
        }
        else if (string.Equals(args[0], "ingest", StringComparison.OrdinalIgnoreCase))
        {
            effectiveArgs = args[1..];
        }
        else if (args[0].StartsWith("-", StringComparison.Ordinal))
        {
            effectiveArgs = args;
        }
        else
        {
            error.WriteLine($"Unknown command '{args[0]}'.");
            MediaIngestCommand.WriteUsage(error);
            return 1;
        }

        return await MediaIngestCommand.RunAsync(effectiveArgs, output, error, cancellationToken).ConfigureAwait(false);
    }
}
