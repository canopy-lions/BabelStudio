using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.TestDoubles;

public sealed class FakeTranslationEngine : ITranslationEngine, ITranslationExecutionMetadataReporter
{
    private readonly Func<TranslationRequest, TranslationInputSegment, string> textFactory;
    private readonly Func<TranslationRequest, TranslationExecutionMetadata?> metadataFactory;

    public FakeTranslationEngine(
        Func<TranslationRequest, TranslationInputSegment, string>? textFactory = null,
        Func<TranslationRequest, TranslationExecutionMetadata?>? metadataFactory = null)
    {
        this.textFactory = textFactory ?? DefaultTextFactory;
        this.metadataFactory = metadataFactory ?? DefaultMetadataFactory;
    }

    public TranslationExecutionMetadata? LastExecutionMetadata { get; private set; }

    public Task<IReadOnlyList<TranslatedTextSegment>> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Segments);

        LastExecutionMetadata = metadataFactory(request);
        IReadOnlyList<TranslatedTextSegment> translatedSegments = request.Segments
            .OrderBy(segment => segment.Index)
            .Select(segment => new TranslatedTextSegment(
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                textFactory(request, segment)))
            .ToArray();

        return Task.FromResult(translatedSegments);
    }

    private static string DefaultTextFactory(TranslationRequest request, TranslationInputSegment segment) =>
        request.TargetLanguage switch
        {
            "es" => $"Segmento generado {segment.Index + 1}.",
            "en" => $"Generated translation {segment.Index + 1}.",
            _ => $"[TRANSLATED] {segment.Text}"
        };

    private static TranslationExecutionMetadata? DefaultMetadataFactory(TranslationRequest request) => null;
}
