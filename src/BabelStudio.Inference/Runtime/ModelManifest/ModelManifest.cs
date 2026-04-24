namespace BabelStudio.Inference.Runtime.ModelManifest;

public sealed record ModelManifestCatalog(
    IReadOnlyList<ModelManifest> Models);

public sealed record ModelManifest(
    string ModelId,
    ModelTask Task,
    ModelLicenseKind License,
    bool CommercialAllowed,
    bool RedistributionAllowed,
    bool RequiresAttribution,
    bool RequiresUserConsent,
    bool VoiceCloning,
    bool CommercialSafeMode,
    string SourceUrl,
    string Revision,
    string Sha256,
    IReadOnlyList<ModelVariantManifest> Variants,
    IReadOnlyList<string> Aliases,
    string? RootPath,
    string? BenchmarkEntry,
    HashVerificationPolicy HashVerificationPolicy);

public sealed record ModelVariantManifest(
    string Alias,
    string EntryPath,
    string Sha256);

public sealed record HashVerificationPolicy(
    HashVerificationMode Mode,
    string Algorithm);

public enum HashVerificationMode
{
    None,
    VerifyIfShaPresent,
    Required
}

public enum ModelTask
{
    Asr,
    Translation,
    Tts,
    Diarization,
    Vad,
    Separation
}

public enum ModelLicenseKind
{
    Mit,
    Apache20,
    CcBy40,
    CcByNc40,
    Custom,
    Unknown,
    NonCommercial
}

internal static class ModelManifestText
{
    public static string ToManifestValue(this ModelTask task) =>
        task switch
        {
            ModelTask.Asr => "asr",
            ModelTask.Translation => "translation",
            ModelTask.Tts => "tts",
            ModelTask.Diarization => "diarization",
            ModelTask.Vad => "vad",
            ModelTask.Separation => "separation",
            _ => throw new ArgumentOutOfRangeException(nameof(task), task, "Unknown model task.")
        };

    public static string ToManifestValue(this ModelLicenseKind license) =>
        license switch
        {
            ModelLicenseKind.Mit => "MIT",
            ModelLicenseKind.Apache20 => "Apache-2.0",
            ModelLicenseKind.CcBy40 => "CC-BY-4.0",
            ModelLicenseKind.CcByNc40 => "CC-BY-NC-4.0",
            ModelLicenseKind.Custom => "custom",
            ModelLicenseKind.Unknown => "unknown",
            ModelLicenseKind.NonCommercial => "non-commercial",
            _ => throw new ArgumentOutOfRangeException(nameof(license), license, "Unknown model license.")
        };

    public static ModelTask ParseTask(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.ToLowerInvariant() switch
        {
            "asr" => ModelTask.Asr,
            "translation" => ModelTask.Translation,
            "tts" => ModelTask.Tts,
            "diarization" => ModelTask.Diarization,
            "vad" => ModelTask.Vad,
            "separation" => ModelTask.Separation,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown model task.")
        };
    }

    public static ModelLicenseKind ParseLicense(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.ToLowerInvariant() switch
        {
            "mit" => ModelLicenseKind.Mit,
            "apache-2.0" => ModelLicenseKind.Apache20,
            "apache-2" => ModelLicenseKind.Apache20,
            "apache2" => ModelLicenseKind.Apache20,
            "apache2.0" => ModelLicenseKind.Apache20,
            "cc-by-4.0" => ModelLicenseKind.CcBy40,
            "cc-by-4" => ModelLicenseKind.CcBy40,
            "ccby-4.0" => ModelLicenseKind.CcBy40,
            "ccby4" => ModelLicenseKind.CcBy40,
            "ccby40" => ModelLicenseKind.CcBy40,
            "cc-by-nc-4.0" => ModelLicenseKind.CcByNc40,
            "cc-by-nc-4" => ModelLicenseKind.CcByNc40,
            "ccbync-4.0" => ModelLicenseKind.CcByNc40,
            "ccbync4" => ModelLicenseKind.CcByNc40,
            "ccbync40" => ModelLicenseKind.CcByNc40,
            "custom" => ModelLicenseKind.Custom,
            "unknown" => ModelLicenseKind.Unknown,
            "non-commercial" => ModelLicenseKind.NonCommercial,
            "noncommercial" => ModelLicenseKind.NonCommercial,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown model license.")
        };
    }
}
