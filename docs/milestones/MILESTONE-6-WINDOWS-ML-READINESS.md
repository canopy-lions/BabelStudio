# Milestone 6 Windows ML Readiness Note

- Milestone: 6
- Title: Transcript-only vertical slice
- Scope: Windows ML / ONNX runtime readiness
- Status: Ready to proceed
- Date: 2026-04-20

## Summary

Windows ML runtime viability is now proven in the real Windows-targeted benchmark harness on this machine.

This is not Milestone 6 completion. It is a runtime-readiness checkpoint confirming that the intended Windows ML + ONNX path can bootstrap successfully, register execution providers, and run bundled benchmark models through the concrete `BabelStudio.Inference.Onnx` layer.

The main conclusion is:

> Milestone 1 viability is reaffirmed, and the Milestone 6 VAD/ASR runtime path is credible enough to begin real stage-engine integration.

## Commands executed

The following commands were run from `D:\Dev\BabelStudio` using the Windows-targeted harness executable:

```powershell
.\src\BabelStudio.Benchmarks\bin\Debug\net10.0-windows10.0.19041.0\BabelStudio.Benchmarks.exe --model silero-vad --runs 1 --provider auto --format console --output .\artifacts\benchmark-windowsml-smoke.json
```

```powershell
.\src\BabelStudio.Benchmarks\bin\Debug\net10.0-windows10.0.19041.0\BabelStudio.Benchmarks.exe --model .\models\silero-vad\onnx\model.onnx --provider cpu --runs 3 --format both --output .\artifacts\silero-cpu.json
```

```powershell
.\src\BabelStudio.Benchmarks\bin\Debug\net10.0-windows10.0.19041.0\BabelStudio.Benchmarks.exe --model whisper-tiny-onnx --provider auto --runs 1 --format both --output .\artifacts\whisper-auto.json
```

## Observed results

### Silero VAD, auto provider

- Status: `Completed`
- Requested provider: `auto`
- Selected provider: `dml`
- Windows ML note: `Windows ML bootstrap succeeded via EnsureAndRegisterCertified.`
- Cold load: `367.36 ms`
- Warm latency avg: `1.02 ms`
- Audio duration: `0.032 s`
- Real-time factor: `0.032x`

### Silero VAD, CPU provider

- Status: `Completed`
- Requested provider: `cpu`
- Selected provider: `cpu`
- Windows ML note: `Windows ML bootstrap skipped for CPU-only provider route.`
- Cold load: `109.66 ms`
- Warm latency avg: `0.19 ms`
- Audio duration: `0.032 s`
- Real-time factor: `0.006x`

### Whisper Tiny ONNX, auto provider

- Status: `Completed`
- Requested provider: `auto`
- Selected provider: `dml`
- Windows ML note: `Windows ML bootstrap succeeded via EnsureAndRegisterCertified.`
- Cold load: `796.56 ms`
- Warm latency avg: `15.54 ms`
- Audio duration: `30.000 s`
- Real-time factor: `0.001x`

## Interpretation

- The Windows-targeted harness is now executing through the Windows ML bootstrap path instead of the earlier deferred shim path.
- `auto` successfully resolves to `dml` for both Silero VAD and Whisper Tiny on this machine.
- The CPU path remains functional and provides a fallback baseline.
- The ONNX Runtime warning about some nodes not being assigned to the preferred execution provider is not a failure by itself; ORT commonly leaves helper or shape-related nodes on CPU.

## What this proves

- The repo can build a Windows-targeted executable host that consumes the Windows-targeted `BabelStudio.Inference.Onnx` asset.
- Windows ML execution-provider bootstrap works in the unpackaged benchmark host.
- The current model/runtime/package alignment is sufficient to run:
  - Silero VAD
  - Whisper Tiny ONNX
- DirectML execution is available through the intended C# runtime path.

## What this does not prove yet

- It does not complete Milestone 6.
- It does not prove full stage orchestration, artifact persistence, transcript editing, or stage-run persistence in the app.
- It does not prove every bundled model will run through the same runtime path.
- It does not prove packaging or clean-machine deployment behavior.

## Practical next step

Begin Milestone 6 engine integration using `BabelStudio.Inference.Onnx` as the concrete runtime layer for:

1. Silero VAD stage execution
2. Whisper ASR stage execution
3. stage-run logging of requested provider, selected provider, and bootstrap outcome

Prefer invoking the built `.exe` directly for manual harness checks after `dotnet build`, because `dotnet run` may occasionally hit transient Windows Defender file-lock contention during rebuilds.
