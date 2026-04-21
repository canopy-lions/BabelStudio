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
                [ "opus-en-es", "helsinki-opus-en-es", "opus-es-en", "helsinki-opus-es-en" ],
                [ ExecutionProviderKind.DirectMl, ExecutionProviderKind.Cpu ],
                [ "merged-decoder", "fp16" ],
                [ "merged-decoder", "fp16" ])
        };
}
