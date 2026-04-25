using BabelStudio.Domain;
using BabelStudio.Inference.Runtime.ModelManifest;

namespace BabelStudio.Inference.Runtime.Planning;

public sealed record StageRuntimeRequirements(
    RuntimeStage Stage,
    ModelTask RequiredTask,
    IReadOnlyList<string> PreferredModelAliases,
    IReadOnlyList<ExecutionProviderKind> AllowedProvidersThisMilestone,
    IReadOnlyList<string> PreferredGpuVariants,
    IReadOnlyList<string> PreferredCpuVariants);

internal static class Milestone5PlanningPolicy
{
    public static IReadOnlyList<ExecutionProviderKind> SupportedProvidersThisMilestone { get; } =
    [
        ExecutionProviderKind.DirectMl,
        ExecutionProviderKind.Cpu
    ];
}

internal static class Milestone5StageRuntimeRequirementsCatalog
{
    public static IReadOnlyDictionary<RuntimeStage, StageRuntimeRequirements> All { get; } =
        new Dictionary<RuntimeStage, StageRuntimeRequirements>
        {
            [RuntimeStage.Vad] = new(
                RuntimeStage.Vad,
                ModelTask.Vad,
                [ "silero-vad", "silero" ],
                [ ExecutionProviderKind.DirectMl, ExecutionProviderKind.Cpu ],
                [ "fp16", "q4f16" ],
                [ "int8", "quantized", "uint8", "q4" ]),
            [RuntimeStage.Asr] = new(
                RuntimeStage.Asr,
                ModelTask.Asr,
                [ "whisper-tiny-onnx", "whisper-tiny", "whisper-tiny-local" ],
                [ ExecutionProviderKind.DirectMl, ExecutionProviderKind.Cpu ],
                [ "fp16", "q4f16" ],
                [ "int8", "quantized", "uint8", "q4" ]),
            [RuntimeStage.Translation] = new(
                RuntimeStage.Translation,
                ModelTask.Translation,
                [ "opus-en-es", "helsinki-opus-en-es", "opus-en-fr", "opus-en-de", "opus-en-it", "opus-en-pt", "opus-es-en", "helsinki-opus-es-en", "madlad400-mt", "madlad400" ],
                [ ExecutionProviderKind.DirectMl, ExecutionProviderKind.Cpu ],
                [ "merged-decoder", "fp16" ],
                [ "merged-decoder", "fp16" ]),
            [RuntimeStage.Diarization] = new(
                RuntimeStage.Diarization,
                ModelTask.Diarization,
                [ "sortformer-diarizer-4spk-v1", "sortformer-4spk", "nvidia-sortformer-diarizer-4spk-v1" ],
                [ ExecutionProviderKind.DirectMl, ExecutionProviderKind.Cpu ],
                [ "default" ],
                [ "default" ]),
            // DirectML excluded: Kokoro ConvTranspose op is incompatible with DirectML EP (upstream ONNX issue, unresolved)
            [RuntimeStage.Tts] = new(
                RuntimeStage.Tts,
                ModelTask.Tts,
                [ "kokoro-v1.0", "kokoro" ],
                [ ExecutionProviderKind.Cpu ],
                [],
                [ "quantized" ])
        };
}
