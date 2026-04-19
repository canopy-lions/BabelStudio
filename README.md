# Babel Studio

Babel Studio is a Windows-native, local-first AI dubbing workstation.

The long-term goal is to load local video, generate timed transcripts, translate dialogue, assign or synthesize voices, preview dubbed speech in context, and export a dubbed result while preserving clear stage state and project artifacts.

This repository is intentionally structured around product boundaries:

- `BabelStudio.App` owns the WinUI 3 shell.
- `BabelStudio.Domain` owns pure domain concepts.
- `BabelStudio.Application` owns use cases and orchestration.
- `BabelStudio.Infrastructure` owns persistence, settings, files, logs, and diagnostics.
- `BabelStudio.Media` owns media probing, extraction, playback support, waveform generation, and muxing.
- `BabelStudio.Inference` owns runtime/model abstractions.
- `BabelStudio.Inference.Onnx` owns concrete ONNX/Windows ML model wrappers.
- `BabelStudio.Benchmarks` proves model/runtime viability before the app depends on it.

## Current build philosophy

Do not start with the full UI. Start with the harness.

Recommended first milestone:

1. Prove Windows ML / ONNX model loading from C#.
2. Record benchmark results to SQLite.
3. Probe media and extract normalized audio.
4. Build a transcript-only vertical slice.
5. Only then expand into translation, TTS, diarization, mix, and export.

## Licensing

Babel Studio Community Edition is intended to be licensed under **GPL-3.0-or-later**.

A separate commercial license may be offered by the copyright holder for organizations that want to use, modify, embed, or redistribute Babel Studio without GPL obligations.

See:

- `LICENSE`
- `COMMERCIAL-LICENSE.md`
- `CONTRIBUTOR-LICENSE-AGREEMENT.md`
- `THIRD_PARTY_NOTICES.md`
- `MODEL_LICENSE_POLICY.md`

Third-party model weights, voices, ONNX exports, binaries, and provider terms are not automatically covered by the app license. Each must be tracked separately in the model/license manifest.

## Monetization structure

Recommended structure:

- Free GPL community edition
- Donations / GitHub Sponsors / Ko-fi
- Paid commercial licenses for non-GPL organizational use
- Optional paid cloud credits/services later, if cloud providers are added
- Optional support contracts for businesses

Do not paywall core safety features such as project persistence, export, diagnostics, or license visibility.

## Repository creation

If this scaffold was downloaded as a ZIP, initialize and push with:

```powershell
git init
git add .
git commit -m "Initial Babel Studio scaffold"
gh repo create canopy-lions/BabelStudio --public --description "Windows-native, local-first AI dubbing workstation built around staged artifacts, ONNX/Windows ML inference, and editable preview." --source . --remote origin --push
```

Use `--private` instead of `--public` if you are not ready to publish.
