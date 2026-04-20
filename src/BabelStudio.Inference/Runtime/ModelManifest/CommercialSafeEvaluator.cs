namespace BabelStudio.Inference.Runtime.ModelManifest;

public sealed class CommercialSafeEvaluator
{
    public CommercialSafeEvaluation Evaluate(ModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var reasons = new List<string>();
        if (manifest.License is ModelLicenseKind.Unknown)
        {
            reasons.Add("Unknown-license models are not commercial-safe.");
        }

        if (manifest.License is ModelLicenseKind.NonCommercial)
        {
            reasons.Add("Non-commercial models are not commercial-safe.");
        }

        if (!manifest.CommercialAllowed)
        {
            reasons.Add("Manifest does not allow commercial use.");
        }

        if (manifest.VoiceCloning && !manifest.RequiresUserConsent)
        {
            reasons.Add("Voice-cloning models must require user consent.");
        }

        return new CommercialSafeEvaluation(
            IsCommercialSafe: reasons.Count == 0,
            RequiresUserConsent: manifest.RequiresUserConsent || manifest.VoiceCloning,
            RequiresAttribution: manifest.RequiresAttribution,
            Reasons: reasons);
    }
}

public sealed record CommercialSafeEvaluation(
    bool IsCommercialSafe,
    bool RequiresUserConsent,
    bool RequiresAttribution,
    IReadOnlyList<string> Reasons);
