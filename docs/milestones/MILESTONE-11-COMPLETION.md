# Milestone 11 Completion - Stock Voice TTS with Kokoro

Date: 2026-04-25

## Scope completed

- `KokoroTtsEngine` reuses a lazy pinned ONNX `InferenceSession` instead of
  creating a fresh session for each `SynthesizeAsync` call.
- `EspeakNgPhonemizer` now resolves installer-bundled `espeak-ng.exe` paths
  before falling back to `PATH`.
- `StartTtsStageHandler` generates per-speaker TTS takes from translated
  segments, writes WAV artifacts, registers metadata, and records `tts` stage
  runs.
- Voice assignments and TTS takes now persist in SQLite through application
  repository interfaces.
- The WinUI shell exposes Kokoro voice assignment, speaker-scoped TTS
  generation, stale batch regeneration, per-segment audition, duration warnings,
  and voice-language mismatch warnings.

## Acceptance coverage

- Speaker-to-voice assignment is persisted in `VoiceAssignments`.
- Generated takes include duration samples, sample rate, provider, model ID,
  voicepack ID, artifact path, and duration-overrun ratio.
- Translated-text edits and voice assignment changes mark affected takes stale.
- Batch regeneration creates fresh takes for stale speaker segments.
- Duration warnings surface in segment rows and the waveform strip.
- Voicepack language mismatch warnings are non-blocking.

## Verification

```text
dotnet test tests\BabelStudio.Application.Tests\BabelStudio.Application.Tests.csproj -m:1 -p:Platform=x64
dotnet test tests\BabelStudio.Infrastructure.Tests\BabelStudio.Infrastructure.Tests.csproj -m:1 -p:Platform=x64
dotnet test tests\BabelStudio.App.Tests\BabelStudio.App.Tests.csproj -m:1 -p:Platform=x64
dotnet test tests\BabelStudio.Domain.Tests\BabelStudio.Domain.Tests.csproj -m:1 -p:Platform=x64
dotnet test tests\BabelStudio.Inference.Tests\BabelStudio.Inference.Tests.csproj -m:1 -p:Platform=x64 --filter "FullyQualifiedName~KokoroHelperComponentTests"
dotnet build src\BabelStudio.App\BabelStudio.App.csproj -m:1 -p:Platform=x64
dotnet build BabelStudio.sln -m:1 -p:Platform=x64
dotnet test BabelStudio.sln -m:1 -p:Platform=x64
```

The full solution test run passed with three expected missing-fixture skips in
the inference test project.
