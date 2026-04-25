# ADR-0005: Kokoro-82M ONNX TTS Architecture for M11

- Status: Accepted
- Date: 2026-04-24

## Context

Milestone 11 introduces stock-voice text-to-speech synthesis. The goal is to
produce per-segment dubbed audio from the translated transcript, using a locally
runnable model with no external API dependency.

The candidate model is **Kokoro-82M** — a lightweight, high-quality English TTS
model available as ONNX via `onnx-community/Kokoro-82M-v1.0-ONNX` (Apache-2.0).

Key constraints:
- The model must run offline on a developer laptop with no GPU required.
- The inference stack must integrate with the existing `IRuntimePlanner` /
  `BenchmarkModelPathResolver` pipeline already used by Whisper, Madlad, and
  SortFormer.
- G2P (grapheme-to-phoneme) must produce IPA phonemes for Kokoro's character-
  level tokenizer.
- Phase A and B deliverables must ship without the model on disk (test doubles
  replace the engine; bundled-model tests skip when model is absent).

---

## Decision 1 — Model source: `onnx-community/Kokoro-82M-v1.0-ONNX`

`hexgrad/Kokoro-82M` ships weights as PyTorch `.pt` files only. The ONNX
community mirror (`onnx-community/Kokoro-82M-v1.0-ONNX`) provides a
pre-converted `model.onnx` / `model_quantized.onnx` with three inputs
(`input_ids`, `style`, `speed`) and one `audio` output. This is the correct
source for ONNX Runtime inference.

## Decision 2 — DirectML excluded (CPU only for M11)

During the M11 spike the DirectML execution provider returned `0x80070057` for
the `ConvTranspose` operator in Kokoro's mel decoder. This is a known upstream
ONNX Runtime DirectML issue unrelated to BabelStudio. CPU execution completes
successfully.

`StageRuntimeRequirements` for `RuntimeStage.Tts` therefore lists only
`ExecutionProviderKind.Cpu` in `AllowedProvidersThisMilestone`. This restriction
will be revisited when the upstream fix ships.

## Decision 3 — G2P approach: espeak-ng subprocess

Kokoro requires IPA phonemes as input. Two viable approaches were evaluated:

| | **subprocess `espeak-ng.exe`** | **KokoroSharp 0.6.7 (P/Invoke DLL)** |
|---|---|---|
| Latency | ~10–50 ms per call | < 5 ms |
| Self-contained | No — requires `espeak-ng` on PATH or bundled separately | Yes — `libespeak-ng.dll` ships inside NuGet |
| License | GPL-3.0-or-later (process boundary = mere aggregation) | **GPL-3.0-or-later propagates in-process** |
| Commercial safe | Yes (mere aggregation) | **No — combined work under FSF GPL rules** |

**KokoroSharp 0.6.7 bundles `libespeak-ng.dll` (GPL-3.0-or-later) and loads
it in-process via P/Invoke.** The FSF treats dynamic linking as a combined work.
Shipping BabelStudio with that DLL present would force the entire application
binary to re-license under GPL-3.0-or-later.

**Decision: spawn `espeak-ng.exe` as a child process.** The process boundary
constitutes "mere aggregation" under the GPL and does not propagate copyleft
to BabelStudio. The `EspeakNgPhonemizer` class encapsulates this and accepts a
configurable executable path to support bundled or PATH-resolved installations.

## Decision 4 — Voicepack style vector slicing

Kokoro ships 56 voicepack `.bin` files (e.g. `af_heart.bin`) under a `voices/`
subdirectory. Each file stores a matrix of shape `(N, 256)` as raw little-endian
float32. At inference, the style tensor `[1, 256]` is the row at index
`tokenCount`, where `tokenCount` equals the full `input_ids` sequence length
(including BOS/EOS `$` tokens). `KokoroVoicepackLoader` implements this slice.

## Decision 5 — Tokenizer: character-level from `tokenizer.json`

The `tokenizer.json` bundled with the model contains a `model.vocab` map of
Unicode characters (IPA symbols, ASCII, punctuation) to integer token IDs. A
`$` token (ID 0) wraps both ends of the sequence per the post-processor. The
`KokoroTokenizer` class loads this map at synthesis time and truncates at 512
tokens.

## Decision 6 — Phase B defers session caching

`KokoroTtsEngine` creates and disposes an `InferenceSession` on each
`SynthesizeAsync` call, following the Madlad/OpusMt engine pattern. Loading a
150 MB ONNX model per call is acceptable for Phase B where correctness is the
goal. A lazy-load / pin-session optimization is planned for Phase C.

---

## Consequences

- `EspeakNgPhonemizer` requires `espeak-ng.exe` to be installed. For developer
  builds this means running `winget install eSpeak-NG.eSpeak-NG`. A bundled
  binary path may be added to the installer in Phase C.
- DirectML will not accelerate Kokoro until the upstream ONNX ConvTranspose fix
  lands. Latency on CPU for a 5-second segment is ~200–400 ms on a modern
  laptop.
- **KokoroSharp must not be added as a dependency without first resolving the
  GPL contamination** (strip `libespeak-ng.dll` from the published artifact or
  switch to GPL-only licensing).
- The `KokoroVoiceCatalog` naming parser supports `a`/`b`/`e`/`f`/`h`/`i`/`j`/
  `k`/`p`/`r`/`z` locale prefixes; unknown prefixes map to `"unknown"` and
  remain discoverable.
