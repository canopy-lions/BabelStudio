# Babel Studio

**Babel Studio** is a Windows-native, local-first AI dubbing workstation.

The goal is to help users load local video, generate timed transcripts, translate dialogue, assign or synthesize voices, preview dubbed speech in context, and export a dubbed result without turning the workflow into a black box.

Babel Studio is designed around staged artifacts, editable pipeline state, and hardware-aware local inference using ONNX / Windows ML where practical.

> Status: early architecture / prototype phase. This repository is not yet a finished dubbing product.

---

## Why Babel Studio exists

Most AI dubbing tools hide the pipeline. That makes demos look smooth, but it makes real editing painful.

Babel Studio is built around a different assumption:

> Dubbing is an editorial workflow, not a one-click conversion.

A useful dubbing app needs to preserve intermediate work:

- source media metadata
- extracted and normalized audio
- speech regions
- speaker labels
- timed transcript segments
- translations
- voice assignments
- TTS takes
- mix plans
- export artifacts

If something goes wrong, the user should be able to fix the stage that caused it instead of starting over.

---

## Product principles

Babel Studio should be:

- **Windows-native**  
  Built for native desktop use, not as a browser wrapper.

- **Local-first**  
  Local media and local inference are primary paths. Cloud providers may exist later, but uploads should never be silent.

- **Stage-aware**  
  Ingest, transcription, translation, speaker assignment, TTS, mix, and export are separate stages with visible state.

- **Resumable**  
  Projects should reopen without recomputing everything.

- **Truthful**  
  No fake readiness. No misleading “GPU enabled” claims without a real model/provider health check.

- **Hardware-aware**  
  The app should use available acceleration intelligently, but users should not need to understand CUDA, TensorRT, DirectML, or model placement just to start.

- **License-aware**  
  Model, voice, provider, and binary licenses must be tracked explicitly. Commercial-safe mode should not rely on vibes.

- **Editable**  
  AI output is draft material. Users should be able to inspect and refine transcript, translation, timing, speakers, voices, and generated speech.

---

## What Babel Studio is not

Babel Studio is not intended to be:

- a web-first SaaS product
- a generic video editor
- a black-box “upload video, get magic dub” tool
- a voice-cloning toy
- a Python environment management exercise for end users
- a promise of perfect lip sync
- a guarantee that every model is commercially safe

Voice cloning, if supported, must be consent-gated, license-aware, and clearly labeled.

---

## Planned pipeline

The long-term pipeline is staged:

```text
Ingest local media
  ↓
Probe streams and extract working audio
  ↓
Optional stem separation
  ↓
Voice activity detection
  ↓
Speaker diarization
  ↓
Timed transcription
  ↓
Translation
  ↓
Speaker / voice assignment
  ↓
TTS generation
  ↓
Preview and refinement
  ↓
Mix and export
