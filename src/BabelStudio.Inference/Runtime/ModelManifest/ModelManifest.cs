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
            ModelLicenseKind.Custom => "custom",
            ModelLicenseKind.Unknown => "unknown",
            ModelLicenseKind.NonCommercial => "non-commercial",
            _ => throw new ArgumentOutOfRangeException(nameof(license), license, "Unknown model license.")
        };
}
