namespace BabelStudio.Domain;

public enum RuntimeStage
{
    Vad = 1,
    Asr = 2,
    Translation = 3,
    Tts = 4,
    Diarization = 5,
    Separation = 6
}

public enum ExecutionProviderKind
{
    Cpu = 1,
    DirectMl = 2
}

public enum StageRuntimePlanStatus
{
    Ready = 1,
    DownloadRequired = 2,
    Blocked = 3
}

public enum RuntimePlanFallbackCode
{
    ProviderUnavailable = 1,
    ProviderSmokeTestFailed = 2,
    ModelNotCached = 3,
    CommercialSafeExcluded = 4,
    NoCompatibleVariant = 5,
    UnsupportedLanguagePair = 6
}

public enum RuntimePlanWarningCode
{
    CpuFallback = 1,
    AttributionRequired = 2,
    UserConsentRequired = 3,
    CommercialSafeModeActive = 4
}
