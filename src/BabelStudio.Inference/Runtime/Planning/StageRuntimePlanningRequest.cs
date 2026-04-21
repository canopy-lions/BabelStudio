using BabelStudio.Domain;

namespace BabelStudio.Inference.Runtime.Planning;

public sealed record StageRuntimePlanningRequest(
    RuntimeStage Stage,
    bool CommercialSafeMode,
    string? PreferredModelAlias = null,
    string? SourceLanguage = null,
    string? TargetLanguage = null);
