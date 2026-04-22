using BabelStudio.Contracts.Pipeline;
using BabelStudio.Inference.Onnx.Madlad;
using BabelStudio.Inference.Onnx.OpusMt;

namespace BabelStudio.Inference.Onnx.Translation;

public sealed class RoutedTranslationEngine : ITranslationEngine, IStageRuntimeExecutionReporter, ITranslationExecutionMetadataReporter
{
    private readonly ITranslationLanguageRouter translationLanguageRouter;
    private readonly OpusMtTranslationEngine opusMtTranslationEngine;
    private readonly MadladTranslationEngine madladTranslationEngine;

    public RoutedTranslationEngine(
        ITranslationLanguageRouter translationLanguageRouter,
        OpusMtTranslationEngine opusMtTranslationEngine,
        MadladTranslationEngine madladTranslationEngine)
    {
        this.translationLanguageRouter = translationLanguageRouter ?? throw new ArgumentNullException(nameof(translationLanguageRouter));
        this.opusMtTranslationEngine = opusMtTranslationEngine ?? throw new ArgumentNullException(nameof(opusMtTranslationEngine));
        this.madladTranslationEngine = madladTranslationEngine ?? throw new ArgumentNullException(nameof(madladTranslationEngine));
    }

    public StageRuntimeExecutionSummary? LastExecutionSummary { get; private set; }

    public TranslationExecutionMetadata? LastExecutionMetadata { get; private set; }

    public async Task<IReadOnlyList<TranslatedTextSegment>> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        TranslationRouteSelection route = await translationLanguageRouter.ResolveRouteAsync(
            request.SourceLanguage,
            request.TargetLanguage,
            request.CommercialSafeMode,
            cancellationToken).ConfigureAwait(false);
        if (!route.IsAvailable)
        {
            throw new InvalidOperationException(
                route.UnavailableReason ??
                $"Translation route {request.SourceLanguage} -> {request.TargetLanguage} is not available.");
        }

        TranslationRequest routedRequest = request with
        {
            PreferredModelAlias = route.PreferredModelAlias,
            ResolvedModelEntryPath = route.ResolvedModelEntryPath
        };

        ITranslationEngine selectedEngine = route.RoutingKind switch
        {
            TranslationRoutingKind.Direct => opusMtTranslationEngine,
            TranslationRoutingKind.Pivot => madladTranslationEngine,
            _ => throw new InvalidOperationException($"Unsupported translation routing kind '{route.RoutingKind}'.")
        };

        IReadOnlyList<TranslatedTextSegment> translatedSegments = await selectedEngine.TranslateAsync(
            routedRequest,
            cancellationToken).ConfigureAwait(false);

        LastExecutionSummary = selectedEngine is IStageRuntimeExecutionReporter reporter
            ? reporter.LastExecutionSummary
            : null;
        LastExecutionMetadata = new TranslationExecutionMetadata(
            route.ProviderName,
            LastExecutionSummary?.ModelId ?? route.ModelId,
            LastExecutionSummary?.ModelAlias ?? route.PreferredModelAlias,
            LastExecutionSummary?.SelectedProvider,
            route.RoutingKind);
        return translatedSegments;
    }
}
