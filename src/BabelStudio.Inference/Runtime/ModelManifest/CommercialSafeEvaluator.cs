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

        if (manifest.License is ModelLicenseKind.NonCommercial or ModelLicenseKind.CcByNc40)
        {
            reasons.Add("Non-commercial models are not commercial-safe.");
        }

        if (!manifest.CommercialAllowed)
        {
            reasons.Add("Manifest does not allow commercial use.");
        }

        if (!manifest.CommercialSafeMode)
        {
            reasons.Add("Manifest does not mark this model as commercial-safe mode eligible.");
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

    public CommercialSafeEvaluation Evaluate(BundledModelManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var manifest = new ModelManifest(
            entry.ModelId,
            ModelManifestText.ParseTask(entry.Task),
            ModelManifestText.ParseLicense(entry.License),
            entry.CommercialAllowed,
            entry.RedistributionAllowed,
            entry.RequiresAttribution,
            entry.RequiresUserConsent,
            entry.VoiceCloning,
            entry.CommercialSafeMode,
            entry.SourceUrl,
            entry.Revision,
            entry.Sha256,
            entry.Variants.Select(variant => new ModelVariantManifest(
                variant.Alias,
                Path.GetRelativePath(entry.RootDirectory, variant.EntryPath),
                string.Empty)).ToArray(),
            entry.Aliases,
            entry.RootDirectory,
            Path.GetRelativePath(entry.RootDirectory, entry.DefaultBenchmarkEntryPath),
            new HashVerificationPolicy(HashVerificationMode.VerifyIfShaPresent, "SHA-256"));

        return Evaluate(manifest);
    }
}

public sealed record CommercialSafeEvaluation(
    bool IsCommercialSafe,
    bool RequiresUserConsent,
    bool RequiresAttribution,
    IReadOnlyList<string> Reasons);
