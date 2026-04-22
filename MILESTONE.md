# Babel Studio Milestone Plan — M8 Onward (Revised April 2026)

This document replaces the original M8–M20 plan. M0–M7 remain as defined.

## What changed and why

**Real ONNX models throughout.** The codebase drifted ahead of the "fakes first" approach — real Silero VAD and Whisper are integrated in M6, and Opus-MT is running in M7. This revision accepts that drift and requires real model integration at each slice milestone. Fakes are still required, but as CI test doubles alongside real implementations, not as the primary deliverable.

**Fakes policy going forward.** Every AI engine interface (`ITranslationEngine`, `ITtsEngine`, `IDiarizationEngine`, etc.) must have:
- A `Fake{Engine}` implementation in `tests/BabelStudio.TestDoubles/` that returns deterministic, hard-coded output with no I/O or model loading.
- A real ONNX-backed implementation in `src/BabelStudio.Inference.Onnx/`.
- Integration tests that run against the real implementation (gated on model fixture availability, skipped in CI if models are absent).
- Unit/application tests that use the fake exclusively for speed and determinism.

This means agent tasks should always produce both the real engine and the fake alongside it. A milestone is not complete if only one exists.

**Each milestone produces a testable app slice.** Every milestone must deliver UI the developer can exercise directly, following the M6 pattern. Backend-only milestones are not acceptable.

**Video player added as M8.** It was absent from the original plan but is a prerequisite for reviewing dubbing work at any stage from M7 onward.

**Diarization moved to M10** (before TTS) to enable multi-speaker voice assignment before the TTS stage runs.

**Expanded translation routing made explicit in M9.** M7 now covers the real direct English <-> Spanish Opus-MT slice; M9 expands that into broader pair routing and MADLAD pivot support.

**TTS timing reconciliation added as M12.** Duration mismatch between translated speech and original timing has no existing milestone.

**Stem separation moved to M13** (before preview mix) so the mix can use the instrumental track.

**Diagnostics bundle added as M18**, before packaging and alpha.

**Glossary added as M16.**

**Subtitle export and loudness normalization added to the export milestone (M15).**

**Settings and project management added to M8.**

**Segment boundary editor added to M8.**


# MILESTONE.md

Babel Studio is a Windows-native, local-first AI dubbing workstation. The project should be built in thin, verifiable slices rather than as one giant application pass.

The main rule for this plan:

> Prove the runtime and persistence spine before building the full editor.

The product is ambitious enough that the biggest risk is not UI polish. The biggest risks are model/runtime viability, artifact integrity, media sync, licensing, and avoiding false readiness.

\---

## Milestone philosophy

Each milestone should produce one of these:

1. A technical proof that reduces major uncertainty.
2. A durable product foundation.
3. A vertical slice users can actually exercise.
4. A safety/legal/commercial foundation that prevents future rework.

Each milestone should have:

* a goal
* acceptance criteria
* non-goals
* risks
* agent task boundaries
* tests or validation steps

Do not move to feature expansion until the previous milestone has a boring, repeatable success path.

\---

## Windows ML package rule

`Microsoft.Windows.AI.MachineLearning` is a concrete runtime dependency, not a planner or UI dependency.

Add it like this:

* Pin the version in `Directory.Packages.props`.
* Add `<PackageReference Include="Microsoft.WindowsAppSDK.ML" />` only in the project that actually executes ONNX through the Windows ML path.
* Keep Windows ML bootstrap and provider registration inside `src/BabelStudio.Inference.Onnx/`.
* Current C# bootstrap path should follow Microsoft Learn's `ExecutionProviderCatalog` APIs:
`var catalog = ExecutionProviderCatalog.GetDefault();`
then `await catalog.EnsureAndRegisterCertifiedAsync();` for the default online path, or `await catalog.RegisterCertifiedAsync();` when only already-installed providers should be registered.
* Current C# prerequisites from Microsoft Learn are `.NET 8` or greater plus a Windows-specific target framework such as `net8.0-windows10.0.19041.0` or greater.
* For unpackaged executable hosts, add `<WindowsPackageType>None</WindowsPackageType>` so the Windows App SDK bootstrapper is enabled.
* For harness-style local execution where you want the runtime carried with the executable, set `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`.
* The executable host should reference `Microsoft.WindowsAppSDK` so the Windows App SDK runtime/bootstrap path is present. `Microsoft.WindowsAppSDK.ML` alone is not enough for an unpackaged host to run correctly.
* If the repo still targets plain `net10.0`, keep any `WindowsMlExecutionProviderBootstrapper` scaffold in `BabelStudio.Inference.Onnx` as a non-executing shim until the Milestone 6 Windows-targeted TFM step is taken.
* Smallest safe TFM change for Milestone 6:
multi-target `src/BabelStudio.Inference.Onnx/` as `net10.0;net10.0-windows10.0.19041.0`.
Put the real `ExecutionProviderCatalog` implementation only in the Windows target, and keep the base `net10.0` target as a deferred shim so non-Windows-specific consumers do not get forced onto Windows APIs too early.
* Only widen the Windows-specific TFM to `src/BabelStudio.Benchmarks/` when that project needs to actually execute the Windows ML bootstrap path.

Do not add it to:

* `src/BabelStudio.Domain/`
* `src/BabelStudio.Application/`
* `src/BabelStudio.Inference/`
* `src/BabelStudio.Infrastructure/`
* the WinUI app shell

Milestone timing:

* Milestone 1 may reference `Microsoft.Windows.AI.MachineLearning` in the harness only if needed to prove Windows ML viability on real hardware.
* Milestone 5 must not reference or execute `Microsoft.Windows.AI.MachineLearning`; the planner should reason only about `ExecutionProviderKind` and runtime requirements.
* Milestone 6 is the first real product milestone where `Microsoft.Windows.AI.MachineLearning` should become a product dependency, and it should be introduced through `src/BabelStudio.Inference.Onnx/`.

If the repo has already passed Milestone 5, add `Microsoft.Windows.AI.MachineLearning` in the first Milestone 6 commit that introduces real stage execution in `BabelStudio.Inference.Onnx`.

\---

## Milestone 0 — Repository and architecture foundation

### Goal

Create the repo foundation, document architectural boundaries, and prevent coding agents from turning the project into a soup cauldron.

### Deliverables

* Root `README.md`
* `ARCHITECTURE.md`
* `MILESTONE.md`
* `AGENTS.md`
* `LICENSE`
* `COMMERCIAL-LICENSE.md`
* `CONTRIBUTOR-LICENSE-AGREEMENT.md`
* `MODEL\\\_LICENSE\\\_POLICY.md`
* `THIRD\\\_PARTY\\\_NOTICES.md`
* `.gitignore`
* `Directory.Build.props`
* `Directory.Packages.props`
* starter directory layout
* README files in important directories

### Acceptance criteria

* Repo clearly states this is early architecture/prototype work.
* GPLv3 + commercial dual-license intent is documented.
* Agent guardrails exist.
* Model license policy exists before any model is added.
* Directory boundaries are clear enough that Codex/Claude Code can be given scoped tasks.

### Non-goals

* No app UI.
* No model execution.
* No installer.
* No cloud provider work.
* No monetization implementation.

### Agent tasks

Good agent prompts:

```text
Review ARCHITECTURE.md for internal contradictions.
Create README files for missing directories.
Draft ADR-0001 for WinUI 3 + Windows ML.
Draft ADR-0002 for SQLite project persistence.
```

Bad agent prompts:

```text
Build Babel Studio.
Implement the whole pipeline.
Create the full WinUI app.
```

\---

## Milestone 1 — Runtime viability harness

### Goal

Prove that Babel Studio can load and run ONNX models from C# through the intended runtime path before building the real app.

This is the first technical gate.

### Deliverables

Project:

```text
src/BabelStudio.Benchmarks/
src/BabelStudio.Inference/
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Domain/
```

Harness features:

* Load a single ONNX model from disk.
* Select or report execution provider.
* Run cold-load measurement.
* Run warm inference measurement.
* Measure wall-clock latency.
* Measure real-time factor where applicable.
* Record success/failure.
* Emit JSON report.
* Emit console summary.

Initial target models:

1. Silero VAD ONNX
2. one Whisper ONNX export
3. one Opus-MT ONNX pair
4. one stock TTS ONNX candidate
5. optional Chatterbox ONNX spike

### Acceptance criteria

* Harness runs from command line.
* At least one model loads and runs from C#.
* Harness reports provider, load time, inference time, and failure reason.
* Failed model/provider combinations are reported clearly, not swallowed.
* No UI project exists yet.

### Non-goals

* No WinUI.
* No project database.
* No video ingest.
* No full pipeline.
* No voice cloning UX.

### Risks

* Windows ML / ONNX provider availability differs across machines.
* Some ONNX exports may require unsupported ops or custom preprocessing.
* Tokenizer/model glue may be more complex than model loading.
* TTS models may be much heavier than expected.

### Tests

* Unit tests for result formatting.
* Smoke test for one tiny ONNX model.
* Harness regression test using a local fixture model if available.
* Golden report format test.

### Agent tasks

Good agent prompt:

```text
Implement BabelStudio.Benchmarks as a .NET console app that loads a supplied ONNX model path, runs one inference call with dummy or fixture input, reports provider, cold load time, warm latency, and writes benchmark-report.json. Do not create UI.
```

\---

## Milestone 2 — Model manifest and commercial-safe policy

### Goal

Create a first-class model registry before real models become scattered across code.

### Deliverables

Projects:

```text
src/BabelStudio.Inference/
src/BabelStudio.Infrastructure/
tests/BabelStudio.Inference.Tests/
```

Features:

* `ModelManifest` type
* manifest JSON schema
* model task enum
* license metadata
* commercial-safe evaluator
* local model cache record
* hash verification policy
* model variant metadata
* required consent flags
* attribution flags

Example manifest fields:

```json
{
  "model\\\_id": "example/model",
  "task": "asr",
  "license": "MIT",
  "commercial\\\_allowed": true,
  "redistribution\\\_allowed": true,
  "requires\\\_attribution": false,
  "requires\\\_user\\\_consent": false,
  "voice\\\_cloning": false,
  "commercial\\\_safe\\\_mode": true,
  "source\\\_url": "",
  "revision": "",
  "sha256": "",
  "variants": \\\[]
}
```

### Acceptance criteria

* Unknown-license models are not commercial-safe.
* Non-commercial models are not commercial-safe.
* Voice-cloning models require consent.
* Manifests can be loaded and validated.
* Invalid manifests produce useful errors.
* Tests cover commercial-safe logic.

### Non-goals

* No model downloading yet.
* No UI model manager yet.
* No legal finalization.
* No automatic Hugging Face lookup yet.

### Risks

* License metadata can become stale.
* Model cards vary in clarity.
* Exported ONNX models may have different terms than source repos.

### Tests

* Valid manifest test.
* Invalid license test.
* Non-commercial exclusion test.
* Voice-cloning consent-required test.
* Attribution-required test.

### Agent tasks

Good agent prompt:

```text
Implement ModelManifest parsing and CommercialSafeEvaluator. Add tests proving unknown and non-commercial licenses are rejected in commercial-safe mode. Do not add real model downloads.
```

\---

## Milestone 3 — SQLite project spine

### Goal

Create durable project and artifact state before building pipeline stages.

### Deliverables

Projects:

```text
src/BabelStudio.Domain/
src/BabelStudio.Application/
src/BabelStudio.Infrastructure/
tests/BabelStudio.Domain.Tests/
tests/BabelStudio.Infrastructure.Tests/
```

Core tables:

```text
SchemaVersion
Projects
MediaAssets
StageRuns
Artifacts
Speakers
SpeakerTurns
TranscriptRevisions
TranscriptSegments
Words
TranslationRevisions
TranslatedSegments
VoiceAssignments
TtsTakes
MixPlans
Exports
ModelCache
BenchmarkRuns
ConsentRecords
```

Core services:

* database migrator
* connection factory
* project repository
* artifact repository
* stage run repository
* model cache repository
* benchmark repository

### Acceptance criteria

* Database can be created from scratch.
* Migrations apply in order.
* Project can be created.
* Stage run can be created and completed.
* Artifact can be registered with hash/path/kind/provenance.
* Project can be reopened.
* Tests use temporary SQLite files.

### Non-goals

* No UI.
* No media extraction yet.
* No real ONNX inference.
* No export.

### Risks

* Schema gets too broad too early.
* Missing artifact provenance causes rework later.
* Destructive migrations create project corruption risk.

### Tests

* Migration test.
* Project repository test.
* StageRun repository test.
* Artifact repository test.
* Transaction rollback test.
* Schema version test.

### Agent tasks

Good agent prompt:

```text
Implement SQLite migration runner and repositories for Projects, StageRuns, Artifacts, and ModelCache using Dapper. Tests must use temporary database files. Do not create UI.
```

\---

## Milestone 4 — Media ingest and artifact store

### Goal

Open a local media file, probe it, extract working audio, and persist the result as project artifacts.

### Deliverables

Projects:

```text
src/BabelStudio.Media/
src/BabelStudio.Application/
src/BabelStudio.Infrastructure/
tests/BabelStudio.Media.Tests/
```

Features:

* media probe abstraction
* FFmpeg/ffprobe wrapper
* normalized audio extraction
* source media fingerprinting
* waveform summary generation
* artifact folder layout
* atomic artifact writes
* media asset repository integration

Project artifact layout:

```text
ProjectName.babelstudio/
├── babel.db
├── manifest.json
├── media/
│   ├── source-reference.json
│   └── normalized\\\_audio.wav
├── artifacts/
│   ├── audio/
│   └── waveform/
├── logs/
└── temp/
```

### Acceptance criteria

* Local video/audio can be probed.
* Source media metadata is stored.
* Working audio is extracted.
* Audio artifact is registered with hash and duration.
* Waveform summary is generated and registered.
* Project can be reopened and artifacts found.
* Missing source file is reported clearly.

### Non-goals

* No video editor UI.
* No ASR yet.
* No final export.
* No stem separation.

### Risks

* FFmpeg availability and licensing.
* Weird media containers.
* Variable frame rate and stream start offsets.
* Long path issues on Windows.

### Tests

* Probe fixture media.
* Extract fixture audio.
* Hash verification.
* Missing media recovery.
* Artifact atomic write test.

### Agent tasks

Good agent prompt:

```text
Implement MediaProbe and AudioExtractionService using FFmpeg process execution. Store outputs through IArtifactStore. Add tests using tiny sample media. Do not add ASR.
```

\---

## Milestone 5 — Runtime planner and model cache

### Goal

Create a runtime planning layer that decides which model variant and execution provider should be used for a stage.

### Deliverables

Projects:

```text
src/BabelStudio.Inference/
src/BabelStudio.Infrastructure/
tests/BabelStudio.Inference.Tests/
```

Features:

* hardware profile
* execution provider discovery abstraction
* model variant selector
* model cache records
* runtime plan record
* stage runtime requirements
* smoke-test interface
* fallback explanation

Example runtime plan:

```json
{
  "stage": "ASR",
  "model\\\_id": "whisper-large-v3-turbo",
  "variant": "fp16",
  "execution\\\_provider": "DirectML",
  "fallback\\\_reason": null,
  "warnings": \\\[]
}
```

### Acceptance criteria

* Planner can choose between GPU and CPU variants.
* Planner explains fallback reasons.
* Missing model produces a download-needed state.
* Commercial-safe mode affects model selection.
* Runtime plan is serializable for logs/stage runs.

### Non-goals

* No actual model download UI.
* No full benchmark-based preset yet.
* No cloud providers.

### Risks

* Planner becomes speculative and too complex.
* Provider availability is confused with model compatibility.
* Users see “GPU ready” before a real model passes.

### Fakes and test doubles

- `FakeExecutionProviderDiscovery` in `tests/BabelStudio.TestDoubles/`: returns a configurable set of available execution providers (e.g. DirectML available, CPU available, Windows ML unavailable); no DXGI or system calls; used for all planner path tests
- `FakeModelCache` in `tests/BabelStudio.TestDoubles/`: returns configurable installed/missing/corrupt state per model ID and variant; no disk access; used in missing-model and commercial-safe-exclusion tests

### Tests

* GPU available plan test.
* CPU fallback plan test.
* missing model test.
* commercial-safe exclusion test.
* provider smoke-test failure test.

### Agent tasks

Good agent prompt:

```text
Implement RuntimePlanner using fake hardware/model providers. It should produce a StageRuntimePlan with fallback explanations and commercial-safe filtering. Add tests. Do not load real ONNX models.
```

\---

## Milestone 6 — Transcript-only vertical slice

### Goal

Produce the first meaningful product slice: open media, extract audio, run speech detection/transcription, show editable transcript, save/reopen.

### Deliverables

Projects:

```text
src/BabelStudio.App/
src/BabelStudio.Application/
src/BabelStudio.Inference/
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Media/
src/BabelStudio.Infrastructure/
```

Features:

* minimal WinUI shell
* open media command
* project creation
* media ingest
* VAD stage
* ASR stage
* transcript segment storage
* transcript list UI
* manual transcript editing
* stage status display
* reopen project with transcript

### Acceptance criteria

* User can open a media file.
* App creates project.
* App extracts audio.
* App runs VAD/ASR through validated path or test/fake engine.
* Transcript segments appear.
* User can edit transcript text.
* Edits persist.
* Project reopens without recomputing.
* Stage run and artifact provenance are stored.

### Non-goals

* No translation.
* No TTS.
* No voice cloning.
* No fancy timeline.
* No final export.

### Risks

* UI is built too wide too early.
* ASR wrapper not stable.
* Transcript edits overwrite generated provenance.
* Long-running tasks block UI.

### Fakes and test doubles

- `FakeVadEngine` in `tests/BabelStudio.TestDoubles/`: returns a hard-coded list of voiced speech intervals from a fixture JSON; no audio I/O or model loading; used in all application-layer VAD stage tests
- `FakeAsrEngine` in `tests/BabelStudio.TestDoubles/`: returns a deterministic list of `TranscriptSegment` records (fixed text, fixed timestamps) for any audio input; no ONNX session; used in transcript persistence, stage-run commit, and stale-marker tests

### Tests

* Application use case tests with `FakeAsrEngine`.
* Transcript persistence test.
* Stage-run commit test.
* UI smoke test if practical.
* Manual end-to-end test on tiny media.

### Agent tasks

Good agent prompt:

```text
Build a minimal WinUI transcript slice: open existing project, display TranscriptSegments from repository, allow editing text, persist edits. Use fake data first. Do not add translation or TTS.
```

\---

## Milestone 7 — Translation slice

### Goal

Translate English or Spanish transcript segments into editable direct opposite-language draft revisions in the WinUI shell.

### Deliverables

Features:

* transcript language selection (`English` or `Spanish`)
* fixed Milestone 7 direct translation pairs (`English -> Spanish`, `Spanish -> English`)
* translation runtime plan
* translation model resolver
* translation stage run
* translated segment storage
* translation editor
* `Needs Refresh` state when transcript changes
* commercial-safe model filtering

### Acceptance criteria

* User can translate English transcript segments to Spanish.
* User can translate Spanish transcript segments to English.
* Translation results are stored as a revision.
* User can edit translated text.
* Transcript edit marks affected translation `Needs Refresh`.
* Reopen preserves translation.
* Model/provider used is recorded.

### Non-goals

* No TTS.
* No free-form target language picker.
* No multi-language routing beyond direct English <-> Spanish.
* No cloud providers.
* No glossary yet.

### Risks

* Opus-MT pair availability gaps.
* Tokenizer implementation complexity.
* Pivot translation quality issues.
* Users expect perfect translation.

### Fakes and test doubles

- `FakeTranslationEngine` in `tests/BabelStudio.TestDoubles/`: returns a hard-coded translated string per source segment (prefixes text with `[TRANSLATED]`); records the last source text and language pair it was called with; instantaneous; no I/O; used in all translation use case, stale marker, and commercial-safe filtering tests

### Tests

* Translation use case with `FakeTranslationEngine`.
* Stale marker test.
* Provider metadata test.
* Commercial-safe filtering test.

### Agent tasks

Good agent prompt:

```text
Implement TranslationRevision and TranslatedSegment persistence. Add StartTranslationStageHandler using ITranslationEngine fake. Mark translation stale when source transcript segment changes.
```

\---


## Milestone 8 — Video player, segment editor, and project management

### Goal

Integrate a video player so the developer and user can see and hear the source media while working. Add segment boundary editing and project management fundamentals: settings persistence, recent projects, and source file relocation.

This is the first milestone where the app feels like a workstation rather than a form.

### Deliverables

Projects:

```
src/BabelStudio.App/
src/BabelStudio.Media.Playback/
```

Features:

- `MediaPlayerElement` embedded in the main window layout
- `PlaybackService` wrapping `Windows.Media.Playback.MediaPlayer`: Play, Pause, SeekTo(TimeSpan), Speed (0.5×, 1×, 1.25×, 1.5×), Position as observable
- Segment sync: clicking a transcript or translated segment seeks the player to segment start time
- Active segment highlight: playback position drives the currently highlighted segment in both lists
- Source-language subtitle overlay driven by `TranscriptSegments` (plain text, no styling yet)
- Waveform strip below video rendered with Win2D `CanvasControl`, driven by the stored `WaveformSummary` artifact
- Playback position cursor on waveform strip
- Segment boundary visualization: vertical lines at segment start and end times on waveform strip
- Codec/format fallback detection: when `MediaFoundation` cannot open the source file, log the failure and surface a warning in the UI (do not silently fall back to audio-only without telling the user)
- Segment split: split a segment at the current playback cursor position, producing two segments whose timestamps cover the original exactly
- Segment merge: merge two adjacent selected segments, combining text and spanning the outer time range
- Segment time trim: adjust segment start and end times via numeric input or drag handle on waveform strip; validate no overlap with adjacent segments
- `SettingsService`: reads and writes `%LocalAppData%/BabelStudio/settings.json`; covers default source language, default target language, model tier preference, window layout, commercial-safe mode
- Recent projects list: last 10 projects ordered by last-opened timestamp, shown on home screen
- Source file relocation: when source media has moved, detect on project open and prompt user to locate the new path; update `MediaAsset` record

### Models and libraries

- `Microsoft.Graphics.Canvas` (Win2D) — pin version in `Directory.Packages.props`
- `Windows.Media.Playback.MediaPlayer` (WinRT, inbox on Windows 10+)
- `Windows.UI.Xaml.Controls.MediaPlayerElement` (WinUI 3 native control)
- No ONNX models

### Fakes and test doubles

- `FakeMediaPlayer` implementing `IMediaPlayer`: in-memory position tracking, no actual media file access; used in all `PlaybackService` unit tests
- `FakeSettingsService` in `tests/BabelStudio.TestDoubles/`: in-memory settings store; returns configurable defaults; used in all tests that depend on `ISettingsService` without touching the filesystem
- `FakeRecentProjectsRepository` in `tests/BabelStudio.TestDoubles/`: in-memory list of recent project entries; configurable initial state; used for recent-projects ordering and eviction tests
- Existing `FakeTranscriptRepository` covers segment split/merge persistence tests

### Acceptance criteria

- User can open a project and see the source video playing in the app.
- Clicking a transcript segment seeks the video to segment start.
- Active segment highlights during playback.
- Source-language subtitles appear over video during playback.
- Waveform strip renders and the playback cursor moves correctly.
- Segment split produces two valid persisted segments that sum to the original duration.
- Segment merge produces one persisted segment spanning the originals.
- Settings round-trip: change a default, close and reopen, setting is preserved.
- Recent projects list shows last 10, clicking one reopens the project.
- Source file relocation prompt appears and updates the `MediaAsset` record.
- Codec warning surfaces in the UI for a known-incompatible source file.

### Non-goals

- No custom video renderer — use `MediaPlayerElement` as-is.
- No per-word alignment visualization (that is M21).
- No full DAW-style waveform editor (that is M21).
- No audio mixing controls (that is M14).
- No A/V sync correction for source files.

### Risks

- `MediaFoundation` codec coverage on bare Windows builds misses some containers (MKV with H.265, AV1). Test early on real project files.
- `MediaPlayerElement` in WinUI 3 unpackaged apps has known initialization ordering issues with the HWND lifetime.
- Win2D `CanvasControl` performance with long waveforms (> 1 hour) requires bucket virtualization or progressive render.
- Segment time edits that conflict with existing translation/TTS revisions need stale invalidation logic or they will produce invalid downstream takes.

### Tests

- `PlaybackService` unit tests: seek, position advance, speed change — all against `FakeMediaPlayer`.
- Waveform render test: correct bucket count, correct time-to-pixel mapping for a known `WaveformSummary`.
- Segment split: two child segments sum to parent duration; start/end timestamps are contiguous.
- Segment merge: merged text is concatenation; time span covers both originals.
- Segment trim: adjusted time persisted; overlap with adjacent segment rejected.
- Settings round-trip: write and read-back all fields.
- Recent projects ordering: 11th project pushes oldest off the list.
- Source relocation: `MediaAsset.SourcePath` updated correctly after relocation prompt.

### Agent tasks

Good agent prompts:

```text
Implement PlaybackService wrapping Windows.Media.Playback.MediaPlayer behind an IMediaPlayer interface.
Expose Play, Pause, SeekTo(TimeSpan), Speed, and Position as observable. Add FakeMediaPlayer to
tests/BabelStudio.TestDoubles/. Add unit tests for all operations using the fake. Do not add waveform rendering.
```

```text
Implement SegmentSplitHandler and SegmentMergeHandler as application use case handlers.
Split must produce two segments whose start/end times cover the original segment exactly with no gap.
Merge must combine text and span the outer time range. Both must invalidate any downstream
TranslatedSegment and TtsTake records as stale. Add unit tests using existing fake repositories.
```

---

## Milestone 9 — Expanded translation routing with Opus-MT and MADLAD-400

### Goal

Expand the real translation stage beyond M7's direct English <-> Spanish slice. Add broader pair routing, MADLAD pivot fallback, and playback-integrated translation review alongside the video player from M8.

### Deliverables

Projects:

```
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Application/
src/BabelStudio.App/
```

Features:

- `OpusMtTranslationEngine`: expand the existing M7 ONNX encoder-decoder wrapper beyond the direct English <-> Spanish pairs
- `MadladTranslationEngine`: ONNX wrapper for MADLAD-400 as pivot router for language pairs without a direct Opus-MT model
- `TranslationLanguageRouter`: selects direct Opus-MT pair or MADLAD pivot path based on what is installed in the model cache; reports which pairs are unavailable due to missing models
- SentencePiece tokenizer integration via `Microsoft.ML.Tokenizers` for Opus-MT, hardened for broader pair coverage
- Language coverage matrix: a declarative record of which direct pairs are supported and which fall back to pivot; surfaced in the translation language selector
- Translation language selector in UI: dropdown bound to `TranslationLanguageRouter.SupportedTargetLanguages`
- Translated segments shown in a split view synced to the video player: clicking a source segment also shows the translated segment and seeks video
- Translated subtitle overlay: toggle between source and target language subtitles on the video player
- Translation revision metadata persisted: model ID, source segment hash, provider used, timestamp

### Models and libraries

| Model | Source | Notes |
|---|---|---|
| Helsinki-NLP Opus-MT | `Helsinki-NLP/opus-mt-{src}-{tgt}` on Hugging Face | ONNX exported via Optimum; ~300 MB per direction pair |
| MADLAD-400 (3B) | `google/madlad400-3b-mt` | Large; needs quantized int8 ONNX export; ~3–4 GB on disk; VRAM-intensive |
| SentencePiece models | Bundled per Opus-MT pair | Used via `Microsoft.ML.Tokenizers` |

- `Microsoft.ML.Tokenizers` (already pinned)
- `Microsoft.ML.OnnxRuntime.DirectML` for MADLAD GPU path

### Fakes and test doubles

- `FakeTranslationEngine` in `tests/BabelStudio.TestDoubles/`: returns a hard-coded translated string per source segment (e.g. prefixes text with `[TRANSLATED]`); instantaneous; no I/O
- `FakeTranslationLanguageRouter` in `tests/BabelStudio.TestDoubles/`: returns a configurable set of supported language pairs and routing paths (direct or pivot); used in all router logic tests without requiring model presence
- All application-layer use case tests use `FakeTranslationEngine`
- Integration tests with real Opus-MT and MADLAD run only when fixture models are present; skip otherwise

### Acceptance criteria

- Direct language pair (e.g. English → Spanish) routes through Opus-MT.
- Unsupported direct pair routes through MADLAD-400.
- Translated segments appear in split view synced to playback position.
- Translated subtitle overlay toggles correctly.
- Editing a source segment marks the corresponding translated segment stale.
- Language router reports unavailable pairs clearly (model not installed, not just silent fallback).
- Translation revision records model ID and provider used.

### Non-goals

- No glossary enforcement yet (that is M16).
- No cloud translation fallback.
- No multi-language simultaneous output.
- No segment-level re-translation trigger (full-pass only in this milestone).

### Risks

- MADLAD-400 ONNX export is not pre-packaged; manual Optimum conversion step required.
- MADLAD int8 quantization quality may degrade translation significantly for some pairs.
- SentencePiece tokenizer edge cases (unknown tokens, language tags) differ between Python reference and .NET implementation.
- Opus-MT coverage has known gaps for low-resource language pairs.

### Tests

- `OpusMtTranslationEngine` integration test against a tiny ONNX fixture model (encoder + decoder with minimal vocab); skipped if model not present.
- `MadladTranslationEngine` integration test against a quantized fixture; skipped if not present.
- Language router: direct pair selected when available; pivot selected when not; missing-model case reports correctly.
- `FakeTranslationEngine` used for all stale marker, revision persistence, and application use case tests.
- Translated subtitle toggle test on view model.

### Agent tasks

Good agent prompts:

```text
Implement TranslationLanguageRouter that checks the model cache for installed Helsinki-NLP Opus-MT pairs
and falls back to MadladTranslationEngine for unsupported pairs. Expose SupportedTargetLanguages as a
list of (language code, routing path, available) tuples. Add FakeTranslationEngine to
tests/BabelStudio.TestDoubles/. Test all routing paths with fakes.
```

```text
Implement MadladTranslationEngine wrapping the MADLAD-400 ONNX int8 export. Accept a source text and
target language tag, run encoder-decoder inference, decode output tokens. Add integration test using
a tiny fixture (skip if model not present). Do not couple to OpusMt engine.
```

---

## Milestone 10 — Speaker diarization and assignment

### Goal

Identify speakers in the audio and allow the user to assign, merge, rename, and manually correct speaker turns before TTS is run. This milestone must complete before M11 (TTS) because voice assignment depends on speaker identity.

### Deliverables

Projects:

```
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Application/
src/BabelStudio.App/
```

Features:

- `SortFormerDiarizationEngine`: ONNX-backed wrapper for NVIDIA SortFormer speaker diarization model; produces a list of `(startTime, endTime, speakerId)` tuples
- Speaker turn persistence (schema already exists: `Speakers`, `SpeakerTurns`)
- Speaker panel UI: list of detected speakers with auto-assigned labels (Speaker 1, 2, ...); rename, merge, split turn controls
- Rename speaker: updates display name; speaker ID unchanged; all dependent records updated
- Merge speakers: reassigns all turns from one speaker to another; original speaker record deleted
- Split speaker turn at a time point: produces two turns
- Manual segment assignment: user overrides which speaker a segment belongs to, independent of diarization output
- Speaker color coding in waveform strip (speaker lane with distinct colors per speaker)
- Diarization confidence display per turn where model outputs confidence
- Overlapping speech warning: flag turns where model reports overlap
- Reference clip extraction: select a clean clip per speaker for future voice cloning (M17); stored as `ArtifactKind.ReferenceClip`
- Single-speaker / no-diarization path: all segments assigned to Speaker 1 by default; diarization is not required to proceed

### Models and libraries

| Model | Source | Notes |
|---|---|---|
| NVIDIA SortFormer | `nvidia/sortformer-diarizer-4spk-v1` | Supports up to 4 speakers; ONNX export needed from PyTorch checkpoint; ~200 MB |

- `Microsoft.ML.OnnxRuntime.DirectML`
- Silero VAD (already integrated from M6) used as pre-filter input

### Fakes and test doubles

- `FakeDiarizationEngine` in `tests/BabelStudio.TestDoubles/`: returns a hard-coded list of speaker turns from a fixture JSON; no I/O; deterministic
- All merge, rename, split, and assignment use case tests use `FakeDiarizationEngine`
- Integration test with real SortFormer ONNX skipped if model not present

### Acceptance criteria

- Diarization runs and produces speaker turns persisted in `SpeakerTurns`.
- Rename speaker: display name updated across all UI references.
- Merge speakers: all turns reassigned; original speaker deleted.
- Manual segment assignment overrides diarization; override persisted.
- Speaker color visible in waveform strip.
- Diarization failure (model missing) falls back gracefully to single-speaker; no crash.
- Reference clip extracted and registered per speaker.
- Single-speaker workflow works without running diarization at all.

### Non-goals

- No voice cloning using reference clips yet (that is M17).
- No speaker identity claim (no "this is Person X" output surfaced to user).
- No more than 4-speaker support in this milestone.

### Risks

- SortFormer ONNX export from PyTorch requires manual conversion; graph may have unsupported ops in OnnxRuntime.
- Overlapping speech handling is poor in most diarization models; warn but do not fail.
- More than 4 speakers in real content causes silent truncation unless explicitly guarded.
- License terms for SortFormer weights need verification before M22.

### Tests

- `FakeDiarizationEngine` produces deterministic turns from fixture.
- Merge speakers: all turn records reassigned; deleted speaker not returned by repository.
- Rename speaker: display name updated; ID unchanged.
- Manual override: segment speaker ID updated and persisted.
- Reference clip: audio range written and artifact registered with `ArtifactKind.ReferenceClip`.
- Fallback: missing model returns single-speaker result without exception.
- Integration test against real SortFormer ONNX fixture (skipped if absent).

### Agent tasks

Good agent prompt:

```text
Implement SortFormerDiarizationEngine as an ONNX wrapper returning List<SpeakerTurn>. Add
FakeDiarizationEngine to tests/BabelStudio.TestDoubles/ returning a fixture list. Implement
MergeSpeakersHandler and RenameSpeakerHandler. All application tests use the fake. Add integration
test for the real engine that skips when the model fixture is absent. Do not add voice cloning.
```

---

## Milestone 11 — Stock voice TTS with Kokoro

### Goal

Generate dubbed speech using the real Kokoro-82M ONNX model. Replace any stub TTS engine. Produce per-segment audio takes the user can audition at the correct position in the video player.

### Deliverables

Projects:

```
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Application/
src/BabelStudio.App/
```

Features:

- `KokoroTtsEngine`: ONNX-backed wrapper for Kokoro-82M; accepts phoneme or raw text input, returns WAV bytes
- Phonemizer integration: `espeak-ng` subprocess invocation for grapheme-to-phoneme conversion; invoked per segment before Kokoro inference
- Kokoro voice catalog: list of available voicepacks by language and gender, loaded from a manifest file bundled with the model
- Speaker-to-voice assignment UI: assign a Kokoro voicepack to each speaker; persisted in `VoiceAssignments`
- Per-segment TTS generation via `StartTtsStageHandler`
- `TtsTake` storage: duration (samples + sample rate), model ID, voicepack ID, provider used, artifact path
- Audition: play a TTS take in the video player at the segment's original start time in the source timeline
- Duration warning per segment: flag where TTS audio duration exceeds original segment duration by > 10%; surfaced in transcript and waveform strip
- Stale marker: take goes stale when translated text or voice assignment changes
- Batch regeneration: re-run all stale takes for a selected speaker
- Voicepack language guard: warn if assigned voicepack language does not match the target translation language

### Models and libraries

| Model | Source | Notes |
|---|---|---|
| Kokoro-82M | `hexgrad/Kokoro-82M` on Hugging Face | ONNX available; ~330 MB; supports English, Spanish, French, Hindi, Italian, Brazilian Portuguese, Japanese, Chinese (v1.0) |
| Kokoro voicepacks | Bundled per language region | `.pt` style weights embedded in or alongside model |
| espeak-ng | espeak-ng.org; Win32 DLL or subprocess | Required for G2P on non-English targets; must be bundled for M23 packaging |

- `Microsoft.ML.OnnxRuntime` (CPU path is sufficient for TTS; DirectML optional)
- `NAudio` or `Windows.Media.Audio` for WAV format I/O

### Fakes and test doubles

- `FakeTtsEngine` in `tests/BabelStudio.TestDoubles/`: returns a pre-generated tiny silent WAV fixture for any input; exposes `LastInputText` and `LastVoicepack` for assertion; no I/O
- `FakePhonemizer` in `tests/BabelStudio.TestDoubles/`: returns a fixed phoneme string for any input text; used in all tests that must not invoke the espeak-ng subprocess
- `FakeVoiceCatalog` in `tests/BabelStudio.TestDoubles/`: returns a configurable list of voicepack entries by language and gender; used in speaker-to-voice assignment and language mismatch warning tests without loading real voicepack manifests
- All application use case tests (stale markers, batch regeneration, voice assignment) use `FakeTtsEngine`
- Integration test runs real Kokoro ONNX against a short text fixture; skipped if model not present

### Acceptance criteria

- User can assign a Kokoro voicepack to each speaker.
- TTS generates a WAV artifact for a translated segment.
- Generated audio plays back at the correct position in the video player.
- Duration warning appears for segments where TTS exceeds original duration.
- TTS take marked stale when translated text is edited.
- TTS take marked stale when voice assignment changes.
- Batch regeneration re-runs all stale takes for a speaker.
- TTS artifact metadata includes duration, sample rate, model ID, voicepack ID.
- Voicepack language mismatch warning appears but does not block generation.

### Non-goals

- No voice cloning (that is M17).
- No timing stretch (that is M12).
- No multi-voice within a single segment.
- No emotional control beyond Kokoro's native style input.

### Risks

- espeak-ng G2P quality varies significantly by language; some targets may need an alternative phonemizer.
- Kokoro does not cover all target languages from M9; coverage gap must be documented and surfaced to user.
- TTS inference on CPU may be too slow for interactive feedback; DirectML path needed for acceptable latency.
- WAV output sample rate from Kokoro (24 kHz) may not match project sample rate; resampling required.

### Tests

- `KokoroTtsEngine` integration test: real ONNX produces a non-empty WAV with correct sample rate; skipped if model absent.
- Duration metadata test: artifact duration field matches actual WAV sample count / sample rate.
- Stale marker test (fake engine): edited translated text marks take stale.
- Voice assignment change marks affected take stale (fake engine).
- Batch regeneration test: all stale takes for a speaker re-queued (fake engine).
- Voicepack language mismatch: warning emitted, generation not blocked.

### Agent tasks

Good agent prompts:

```text
Implement KokoroTtsEngine wrapping Kokoro-82M ONNX. Accept phoneme input produced by an IGraphemeToPhoneme
interface. Return WAV bytes. Register output as a TtsTake artifact with duration and voicepack ID.
Add FakeTtsEngine and FakePhonemizer to tests/BabelStudio.TestDoubles/. Add integration test skipped
when model fixture is absent.
```

```text
Implement StartTtsStageHandler using ITtsEngine. It should generate takes for all translated segments
for a given speaker, persist TtsTake records, and mark takes stale when source text or voice assignment
changes. All tests use FakeTtsEngine.
```

---

## Milestone 12 — TTS timing reconciliation

### Goal

Address the fundamental dubbing challenge that translated speech often takes more or less time than the original. Deliver duration mismatch visibility and automatic time-stretch for mild overruns.

### Deliverables

Projects:

```
src/BabelStudio.Application/
src/BabelStudio.Media/
src/BabelStudio.App/
```

Features:

- `DurationAnalysisService`: for each `TtsTake`, compute original segment duration vs. TTS audio duration and overrun ratio
- Duration mismatch overlay in waveform strip: segments color-coded by overrun severity (green ≤ 10%, yellow 10–25%, red > 25%)
- Segment inspector panel: shows original duration, TTS duration, overrun %, stretch status, override option
- `AudioTimeStretchService`: wraps FFmpeg `atempo` filter; handles the chain-filter case (two `atempo` filters) required for ratios outside 0.5–2.0×
- Auto-stretch policy: if overrun ≤ 20%, apply stretch automatically after TTS generation; if > 20%, flag and do not auto-stretch
- Stretch metadata in `TtsTake`: stretch ratio applied, pre-stretch duration, whether stretch was manual or automatic
- Manual override: user can force-stretch any take regardless of threshold from the segment inspector
- Speed limit warning: flag when stretch ratio would require > 1.5× acceleration (quality likely to degrade)
- Re-stretch on re-TTS: when translated text changes and TTS is regenerated, stretch recalculated

### Models and libraries

- FFmpeg `atempo` filter (existing FFmpeg wrapper)
- `MathNet.Numerics` for ratio arithmetic

No new ONNX models.

### Fakes and test doubles

- `FakeAudioTimeStretchService` in `tests/BabelStudio.TestDoubles/`: records the stretch ratio it was called with; returns the input WAV unchanged; used in all application-layer tests
- FFmpeg command builder tested independently with known ratio inputs

### Acceptance criteria

- Duration analysis runs after TTS generation and populates overrun ratio on take.
- Segments with overrun > 10% are color-coded in waveform strip.
- Auto-stretch fires for overruns ≤ 20%; stretched WAV replaces artifact.
- Manual stretch available for any take via segment inspector.
- Stretch ratio and pre-stretch duration stored in take record.
- Speed limit warning appears for ratios requiring > 1.5× acceleration.
- Re-translating a segment clears stretch metadata and re-runs analysis after new TTS.

### Non-goals

- No WSOLA or phase-vocoder implementation (FFmpeg `atempo` is sufficient for this milestone).
- No pitch correction.
- No automatic script shortening based on duration budget.

### Risks

- FFmpeg `atempo` quality degrades above 1.5× acceleration; users will notice.
- Very short segments (< 0.5 s) may not stretch cleanly; need minimum duration guard.
- Two-filter atempo chain for > 2× ratios needs correct FFmpeg filter graph syntax.

### Tests

- Overrun ratio calculation: original 2.0 s, TTS 2.3 s → 15.0% overrun.
- Auto-stretch threshold: 15% → stretch applied; 25% → flagged only.
- FFmpeg command builder: correct atempo chain for ratios within and outside 0.5–2.0× range.
- Stretch metadata persisted: ratio and pre-stretch duration on take record.
- Speed-limit warning: ratio > 1.5× triggers warning in analysis result.
- Re-TTS: stretch metadata cleared; re-analysis runs after new take.

### Agent tasks

Good agent prompt:

```text
Implement DurationAnalysisService computing overrun ratio per TtsTake. Implement AudioTimeStretchService
wrapping FFmpeg atempo with correct chain-filter logic for ratios outside 0.5–2.0×. Auto-stretch takes
≤ 20% overrun; flag others. Store stretch ratio and pre-stretch duration in TtsTake. Add
FakeAudioTimeStretchService to tests/BabelStudio.TestDoubles/. Unit test the ratio math and the
FFmpeg command builder separately.
```

---

## Milestone 13 — Stem separation with Demucs

### Goal

Add vocal/instrumental stem separation as an optional quality step that improves ASR input (cleaner speech signal) and the final mix (original music under dubbed speech).

### Deliverables

Projects:

```
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Application/
```

Features:

- `DemucsStemSeparationEngine`: ONNX-backed wrapper for Demucs `htdemucs` model; processes audio in overlapping chunks to handle files of arbitrary length
- Vocals artifact: `ArtifactKind.Vocals`, registered with hash and duration
- Instrumental artifact: `ArtifactKind.Instrumental`, registered with hash and duration
- Stage warning: displayed whenever stems are used — estimates only, not clean dialogue removal
- Bypass option: user can skip stem separation; all downstream stages fall back to full-mix audio
- ASR routing: when vocals artifact is present, `WhisperOnnxAudioTranscriptionEngine` receives the vocals track instead of full mix
- Mix plan routing: when instrumental artifact is present, mix plan (M14) uses instrumental instead of full mix for the source audio lane
- Progress indicator: stem separation is slow; show per-chunk progress in stage status
- Re-run command: user can re-run if source audio is updated

### Models and libraries

| Model | Source | Notes |
|---|---|---|
| Demucs htdemucs | `facebookresearch/demucs` | htdemucs ONNX export required from PyTorch; ~80 MB per stem branch; DirectML GPU strongly recommended |
| Demucs htdemucs_ft | `facebookresearch/demucs` | Fine-tuned variant; higher quality, larger; optional |

- `Microsoft.ML.OnnxRuntime.DirectML` (CPU path is functionally correct but very slow)

### Fakes and test doubles

- `FakeStemSeparationEngine` in `tests/BabelStudio.TestDoubles/`: writes two copies of the input WAV as vocals and instrumental artifacts; deterministic; no inference
- All application use case tests use the fake
- Integration test with real Demucs ONNX skipped if model not present

### Acceptance criteria

- Stem separation produces vocals and instrumental artifacts persisted with correct kinds.
- Vocals artifact used as ASR input when present.
- Bypass: no artifacts produced; downstream stages use full-mix audio without error.
- Instrumental artifact flagged for use in mix plan construction.
- Stage warning visible in UI when stems are used.
- Re-run replaces existing artifact records.

### Non-goals

- No per-instrument stem separation (vocals and instrumental only).
- No mandatory stem separation.
- No promise of clean dialogue removal.

### Risks

- Demucs ONNX export from PyTorch is non-trivial; streaming/chunked inference required for long audio.
- VRAM requirements are substantial; CPU fallback is very slow (potentially hours for a feature film).
- Separation quality degrades on heavily compressed or reverberant source audio.
- License: Demucs is MIT-licensed but exported weights should be verified.

### Tests

- `FakeStemSeparationEngine` produces two artifacts with correct kinds.
- Bypass path: no artifacts produced; ASR receives full-mix artifact.
- ASR routing: vocals artifact selected over full-mix when present.
- Re-run: artifact records updated with new paths and hashes.
- Integration test: real Demucs ONNX produces non-silent WAVs for a fixture audio; skipped if model absent.

### Agent tasks

Good agent prompt:

```text
Implement StemSeparationStage with DemucsStemSeparationEngine ONNX wrapper. Process audio in
overlapping chunks (e.g. 8-second chunks, 1-second overlap). Produce vocals and instrumental
WAV artifacts registered with correct ArtifactKind. Add bypass path. Add FakeStemSeparationEngine
to tests/BabelStudio.TestDoubles/. Add integration test skipped when model absent.
```

---

## Milestone 14 — Preview mix

### Goal

Preview generated dubbed speech against original media in full context. This is the first time the user hears the complete dubbed output: original video, background music (instrumental if stems ran), and dubbed speech together.

### Deliverables

Projects:

```
src/BabelStudio.Application/
src/BabelStudio.Media/
src/BabelStudio.App/
```

Features:

- `MixPlanBuilder`: constructs a `MixPlan` from available artifacts — source audio lane (full mix or instrumental), dubbed speech lane (TTS takes placed at segment start times), gap-fill silence for missing takes
- Source audio lane: prefers `ArtifactKind.Instrumental` if available; falls back to normalized full-mix audio
- Dubbed speech lane: `TtsTake` artifacts placed at segment start times; stale or missing takes produce a silent gap and a warning
- Gain controls: independent volume for source audio lane and dubbed speech lane (persisted in `MixPlan`)
- Source audio ducking: reduce source audio gain during dubbed speech segments by a configurable amount (default: -12 dB); duck range extends slightly beyond segment for natural decay
- `PreviewRangeRenderer`: accepts a time range and `MixPlan`, produces a preview WAV by mixing the two lanes using FFmpeg `amix` or sample-level compositing
- Preview playback: preview WAV plays in the video player with the source video in sync
- Stale/missing take warnings: visible in the preview panel as a list with segment references
- Preview range selector: user defines start/end time of preview range; scrub or type
- `MixPlan` persisted per project: same representation used by export to prevent preview/export drift

### Models and libraries

- FFmpeg `amix`, `adelay`, `volume` filters (existing wrapper)
- `MathNet.Numerics` for sample-level gain if not using FFmpeg filters

No new ONNX models.

### Fakes and test doubles

- `FakeMixRenderer` in `tests/BabelStudio.TestDoubles/`: accepts `MixPlan` and time range; returns a fixed-length silent WAV; records calls for assertion
- `FakeArtifactStore` in `tests/BabelStudio.TestDoubles/`: in-memory implementation of `IArtifactStore`; registers artifact records without writing to disk; configurable to return pre-seeded artifact paths; used in all mix plan and export pipeline tests
- All `MixPlanBuilder` and application tests use `FakeMixRenderer` and `FakeArtifactStore`

### Acceptance criteria

- Preview range generates a WAV and plays in the video player.
- Dubbed speech audible at segment positions.
- Instrumental track used in source lane if stems ran.
- Source audio ducked during speech segments.
- Stale/missing takes produce a silent gap and appear in warnings list.
- `MixPlan` record persisted and loaded correctly on project reopen.
- Preview WAV duration matches requested range within 50 ms.

### Non-goals

- No full-length mix export (that is M15).
- No per-word timing adjustment.
- No reverb or EQ on dubbed speech.
- No advanced DAW controls.

### Risks

- Preview/export drift if `MixPlan` format changes between milestones.
- Timing representation bugs: segment start times in domain model (rational seconds) vs. audio sample positions.
- Audio gaps or overlaps where TTS duration reconciliation (M12) has not been applied.

### Tests

- `MixPlanBuilder`: correct lane assignments for present and missing takes.
- Range render: output WAV is correct duration; clips placed at correct sample offsets.
- Duck gain: source audio amplitude reduced during speech regions.
- Missing take: silent gap inserted; warning entry emitted.
- `MixPlan` persistence round-trip.

### Agent tasks

Good agent prompt:

```text
Implement MixPlanBuilder and PreviewRangeRenderer. Mix source audio lane (preferring instrumental
artifact) with dubbed speech lane using FFmpeg. Place TTS takes at segment start time (convert to
sample offset). Duck source audio gain during speech regions. Fill gaps with silence. Add
FakeMixRenderer to tests/BabelStudio.TestDoubles/. Test timing, gaps, and ducking independently.
```

---

## Milestone 15 — Export with subtitles and loudness normalization

### Goal

Export a complete, delivery-ready dubbed output: video with dubbed audio, subtitle files in standard formats, and loudness normalization to a configurable delivery spec.

### Deliverables

Projects:

```
src/BabelStudio.Application/
src/BabelStudio.Media/
src/BabelStudio.App/
```

Features:

- Full mix render: full-duration `MixPlan` rendered to a final WAV using the same renderer as M14
- Video mux: source video stream copied without re-encode (where codec permits); dubbed audio WAV muxed in via FFmpeg stream copy + audio encode
- Subtitle export:
  - SRT (SubRip): `HH:MM:SS,mmm --> HH:MM:SS,mmm` format
  - VTT (WebVTT): `WEBVTT` header + cue format
  - ASS (Advanced SubStation Alpha): basic style block + timed dialogue events
- Subtitle source: `TranslatedSegments` for dubbed output; `TranscriptSegments` for source-language option
- Subtitle burn-in option: render subtitles into video stream via FFmpeg `subtitles` filter (requires re-encode)
- Loudness normalization: two-pass EBU R128 via FFmpeg `loudnorm` filter; configurable target LUFS (defaults: -14 LUFS for online platforms, -23 LUFS for broadcast)
- Export options panel: subtitle format checkboxes, burn-in toggle, loudness target input, output container selection (MP4, MKV)
- Export manifest: JSON file alongside output recording source project ID, all stage run IDs, all model IDs, TTS voices used, loudness achieved, any warnings
- Metadata embedding: `DUBBED_BY=BabelStudio`, source language, target language written to output container metadata
- Output verification: check output file is playable, duration within 500 ms of source, audio stream present
- Clear failure reporting: missing takes, duration tolerance exceeded, or codec error produce a failure report listing the specific cause; no silent partial output

### Models and libraries

- FFmpeg (existing wrapper): `loudnorm` two-pass, `subtitles` filter, `acodec aac` / `libopus`, `copy` for video stream
- No new ONNX models

### Fakes and test doubles

- `FakeExportRenderer` in `tests/BabelStudio.TestDoubles/`: records `ExportPlan` it was called with; writes a tiny fixture MP4 from pre-generated bytes; used in application export use case tests
- `FakeLoudnessNormalizer` in `tests/BabelStudio.TestDoubles/`: records the LUFS target it was called with; returns input audio path unchanged; used to test export pipeline orchestration without invoking two-pass FFmpeg loudnorm
- FFmpeg command builders tested with unit tests against expected filter graph strings

### Acceptance criteria

- Full-length export produces a playable MP4 or MKV.
- Video stream copied without re-encode for H.264 and H.265 sources.
- SRT, VTT, and ASS subtitle files generated with correct timestamps.
- Loudness normalization applied; achieved LUFS within 1 LU of target.
- Export manifest written alongside output.
- Missing or stale takes cause export failure with a list of affected segment IDs.
- Output duration within 500 ms of source.
- Container metadata includes `DUBBED_BY` tag.

### Non-goals

- No cloud render.
- No simultaneous multi-language export.
- No advanced codec selection.
- No chapter markers.

### Risks

- FFmpeg `loudnorm` two-pass adds significant wall-clock time for long files.
- Stream copy fails for some containers; re-encode required and must not be silent.
- ASS subtitle timing needs careful mapping from rational seconds to ASS centisecond format.
- Missing takes not checked before render starts may produce partial output silently.

### Tests

- SRT generation: known fixture segments produce correct `HH:MM:SS,mmm` timestamps.
- VTT generation: correct `WEBVTT` header and cue format.
- ASS generation: correct style block and timing format.
- Loudnorm command builder: correct two-pass filter graph.
- Export manifest content: all model IDs and stage run IDs present.
- Missing take: export handler returns failure with segment list before starting FFmpeg.
- Output duration verification math.

### Agent tasks

Good agent prompts:

```text
Implement SubtitleExportService producing SRT, VTT, and ASS from TranslatedSegment records.
Test against a fixture segment list with known timestamps. Verify each format independently.
```

```text
Implement ExportPlan and MuxWriter. Full-duration mix render → FFmpeg mux with video copy → loudnorm
two-pass → subtitle sidecar files. Write export manifest JSON. Add FakeExportRenderer to
tests/BabelStudio.TestDoubles/. Add command-builder unit tests for loudnorm and mux commands.
```

---

## Milestone 16 — Glossary and terminology

### Goal

Add a project-level glossary to improve translation consistency for character names, technical terms, and domain vocabulary.

### Deliverables

Projects:

```
src/BabelStudio.Domain/
src/BabelStudio.Application/
src/BabelStudio.Infrastructure/
src/BabelStudio.App/
```

Features:

- `GlossaryEntry` domain record: source term, target term, source language, target language, scope (project or global), case-sensitive flag
- Glossary persistence: project-scoped entries in `babel.db`; global entries in `%LocalAppData%/BabelStudio/global-glossary.json`
- Glossary editor UI: add, edit, delete entries; filter by language pair
- CSV import: bulk import entries from a two-column CSV (source term, target term)
- Translation engine integration: glossary terms passed to `ITranslationEngine` as hint list; engines apply post-substitution where constrained decoding is unavailable
- Glossary match highlighting in the translated segment editor: matched terms underlined
- Conflict detection: warn when a source term appears in multiple entries with different targets for the same language pair

### Models and libraries

No new ONNX models.

### Fakes and test doubles

- `FakeTranslationEngine` (already exists) extended to accept glossary hints and reflect them in output (e.g. replace matching source substrings with the target term)
- All glossary integration tests use `FakeTranslationEngine`

### Acceptance criteria

- Glossary entries round-trip through repository.
- CSV import creates entries from a fixture file.
- Translation output reflects glossary terms (verified with fake engine).
- Glossary terms highlighted in translated segment editor.
- Conflict detection warns on duplicate source terms.
- Project glossary persists with project; global glossary persists across projects.

### Non-goals

- No automatic terminology extraction from source text.
- No translation memory (full-segment matching).
- No multi-variant glossary entries (one target per source per language pair).

### Risks

- Post-substitution approach breaks morphology for inflected languages; acceptable limitation in this milestone.
- Constrained decoding may not be feasible for Opus-MT or MADLAD without model surgery.
- Glossary term case sensitivity handling needs consistent policy.

### Tests

- Glossary persistence: add, retrieve, delete via repository.
- CSV import: known fixture CSV → expected entries.
- Conflict detection: duplicate source term triggers warning.
- Translation integration: glossary hint reflected in fake engine output.
- Global vs. project scope: entries correctly separated.

### Agent tasks

Good agent prompt:

```text
Implement GlossaryEntry persistence and GlossaryRepository (project scope in babel.db, global scope
in settings directory). Add glossary hints parameter to ITranslationEngine. Extend FakeTranslationEngine
to apply hints via simple string substitution. Add glossary editor view model with add/delete/import
commands. Test persistence, CSV import, and conflict detection.
```

---

## Milestone 17 — Voice cloning with Chatterbox

### Goal

Add opt-in, consent-gated voice cloning using Chatterbox TTS. Voice cloning must not be possible to invoke without explicit per-session user consent.

### Deliverables

Projects:

```
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Application/
src/BabelStudio.App/
```

Features:

- Voice cloning consent dialog: modal, shown before first clone operation per session; requires active checkbox confirmation; state stored per project session only (never persisted across launches)
- Per-session cloning warning banner: visible in the main window whenever voice cloning is active this session
- `ChatterboxTtsEngine`: ONNX-backed wrapper for Chatterbox with voice conditioning on a reference clip WAV
- Reference clip selection: user selects a reference clip per speaker from extracted clips (M10) or by manual file selection
- Reference clip replacement: user can swap reference clip and trigger regeneration
- `TtsTakeKind.VoiceCloned` distinguishes cloned takes from stock TTS takes in all records and UI
- Export manifest: records which segments used voice cloning and the reference clip artifact ID for each
- Commercial-safe filtering: Chatterbox model manifest must declare `voice_cloning: true` and `requires_user_consent: true`; blocked in commercial-safe mode unless manifest explicitly allows commercial voice cloning use
- Fallback: when no reference clip is provided, fall back to `KokoroTtsEngine` silently and log the fallback

### Models and libraries

| Model | Source | Notes |
|---|---|---|
| Chatterbox | `resemble-ai/chatterbox` on Hugging Face | Conditional flow matching TTS; ONNX export required; ~750 MB; GPU strongly recommended |

- `Microsoft.ML.OnnxRuntime.DirectML`

### Fakes and test doubles

- `FakeVoiceCloneTtsEngine` in `tests/BabelStudio.TestDoubles/`: extends `FakeTtsEngine`; records reference clip ID it was called with; returns same silent WAV fixture; throws `ConsentRequiredException` if consent state is not set
- `FakeConsentService` in `tests/BabelStudio.TestDoubles/`: in-memory consent state store; consent can be pre-set or pre-cleared in test setup; does not persist to disk; used in all consent-gate and commercial-safe-mode tests
- All consent, commercial-safe, and reference clip tests use the fakes
- Integration test with real Chatterbox ONNX skipped if model not present

### Acceptance criteria

- Voice cloning cannot invoke without consent confirmation in the same session.
- Consent state not persisted to disk; re-required on next launch.
- Reference clip selection and replacement works.
- Cloned TTS take stored with `TtsTakeKind.VoiceCloned` and reference clip artifact ID.
- Export manifest records voice cloning use per segment.
- Commercial-safe mode blocks Chatterbox unless manifest explicitly permits it.
- Fallback to Kokoro occurs when no reference clip is provided; fallback logged.

### Non-goals

- No automatic voice matching.
- No celebrity or public figure detection.
- No guarantee of voice identity fidelity.
- No emotional control beyond Chatterbox's native capability.
- No default voice cloning without explicit user action.

### Risks

- Legal and ethical misuse; consent mechanism must be robust and not bypassable.
- Chatterbox ONNX export quality may be lower than native PyTorch inference.
- High VRAM requirement may exclude users without a modern discrete GPU.
- Reference clip quality directly determines output quality.

### Tests

- Consent required: `FakeVoiceCloneTtsEngine` throws without consent; no take produced.
- Commercial-safe exclusion: Chatterbox model blocked when commercial-safe mode is on.
- Reference clip persistence: selected clip ID stored in `VoiceAssignment`.
- Cloned take artifact: correct kind and reference clip ID in take record.
- Export manifest: voice cloning segments listed with reference clip IDs.
- Fallback: no reference clip → Kokoro engine invoked; fallback logged.
- Integration test: real Chatterbox ONNX produces non-silent WAV from short reference clip (skipped if model absent).

### Agent tasks

Good agent prompt:

```text
Implement voice cloning consent state as a per-session, non-persisted service. Implement
ChatterboxTtsEngine ONNX wrapper with reference clip conditioning. Enforce consent check at
invocation. Add FakeVoiceCloneTtsEngine to tests/BabelStudio.TestDoubles/ that throws
ConsentRequiredException when consent is absent. Add tests for consent, commercial-safe blocking,
fallback, and export manifest.
```

---

## Milestone 18 — Diagnostics bundle

### Goal

Build a structured diagnostics system so runtime failures are diagnosable without the user being present. This must be complete before packaging and public alpha.

### Deliverables

Projects:

```
src/BabelStudio.Infrastructure/
src/BabelStudio.App/
```

Features:

- `DiagnosticsCollector`: aggregates structured log files, DB schema version, model cache state summary (installed/missing/corrupt per model), hardware profile, OS version, Windows App SDK version, ONNX Runtime version, DirectML availability
- Structured log rotation: keep last 10 session log files; older files deleted on startup
- `FailureCategory` enum: `ModelLoadFailure`, `InferenceFailure`, `MediaDecodeFailure`, `PersistenceFailure`, `UiCrash`, `UnknownError`
- `DiagnosticsBundleExporter`: produces a `.zip` containing log files, `diagnostics.json` manifest, DB schema version, model cache summary, hardware info
- Path redaction: any file paths containing the Windows username replaced with `<USER>` before inclusion in bundle
- Log size cap: if a single session log exceeds 50 MB, truncate to last 50 MB before inclusion
- Improved error dialog: all unhandled exceptions produce a dialog with failure classification, a plain-language one-sentence explanation, and an "Export diagnostics" button
- `AppHealthMonitor`: tracks stage completion state for the current session; surfaces a health summary in the settings/about panel
- Diagnostics export command available in main menu at all times, not only on error

### Models and libraries

- `System.IO.Compression.ZipArchive` (.NET built-in)
- No new ONNX models

### Fakes and test doubles

- `FakeDiagnosticsCollector` in `tests/BabelStudio.TestDoubles/`: returns a fixed `DiagnosticsSnapshot` with known values; used in bundle export tests
- `FakeAppHealthMonitor` in `tests/BabelStudio.TestDoubles/`: in-memory implementation of `IAppHealthMonitor`; accepts stage completion and failure events; returns configurable health summary; used in health summary display and settings panel tests
- Path redaction logic tested with known Windows-style paths containing a known username

### Acceptance criteria

- Diagnostics bundle export produces a valid zip containing all required files.
- No Windows username present in any bundle file path.
- All unhandled exceptions surface the improved error dialog with export button.
- App health monitor correctly reflects completed and failed stages for the current session.
- Log rotation: 11th session removes the oldest log file.
- Log size cap: oversized session log truncated before inclusion.
- Bundle accessible from main menu regardless of error state.

### Non-goals

- No automatic telemetry upload.
- No remote crash reporting service.
- No log streaming to external service.

### Risks

- Path redaction may miss edge cases (e.g. UNC paths, environment variable expansion).
- Session log files may be very large for long inference runs.
- Diagnostic bundle itself could be large if many sessions are retained.

### Tests

- Bundle zip contains: session logs, `diagnostics.json`, schema version file, model cache summary.
- Path redaction: `C:\Users\TestUser\AppData\...` → `C:\Users\<USER>\AppData\...`.
- Log rotation: after 11 sessions, only 10 logs present.
- Log size cap: log truncated at correct byte offset.
- Failure classification: known exception types map to correct `FailureCategory`.
- Health monitor: correct stage completion state after sequence of stage handler calls.

### Agent tasks

Good agent prompt:

```text
Implement DiagnosticsCollector and DiagnosticsBundleExporter. Collect structured log files, model
cache summary, DB schema version, and hardware info. Redact Windows username from all paths.
Write to zip. Enforce log rotation (10 sessions) and log size cap (50 MB). Add
FakeDiagnosticsCollector to tests/BabelStudio.TestDoubles/. Test bundle contents, redaction,
rotation, and cap.
```

---

## Milestone 19 — Hardware profiler and preset recommendation

### Goal

Benchmark the user's hardware against real ONNX pipeline workloads and recommend a quality preset.

### Deliverables

Projects:

```
src/BabelStudio.Benchmarks/
src/BabelStudio.Application/
src/BabelStudio.App/
```

Features:

- Benchmark scenarios against real ONNX models (not synthetic):

| Scenario | Model | Metric |
|---|---|---|
| VAD | Silero VAD ONNX | Latency (ms per 30 ms chunk) |
| ASR | Whisper Large-V3-Turbo ONNX | RTF (real-time factor) |
| Translation | Opus-MT single pair | Tokens per second |
| TTS | Kokoro-82M ONNX | RTF |

- Provider under test per scenario: Windows ML (if available and certified), DirectML, CPU ONNX Runtime; best available selected automatically
- RTF calculation: wall-clock inference time / audio duration for ASR and TTS scenarios
- Peak memory measurement: `Windows.System.MemoryManager.AppMemoryUsage` delta during inference
- Results stored in `BenchmarkRuns` (schema already exists)
- Quality preset recommendation: `Quality / Balanced / Turbo / CPU-safe` thresholds defined from RTF and memory results; displayed with explanation
- Hardware profiler tab in the settings panel
- Recommendation overridable by user; override stored in `SettingsService`
- Runtime planner uses stored benchmark results to select provider (feeds into M5 planner)
- Benchmark results invalidated when driver version changes (detect via DXGI adapter description)

### Models and libraries

- All four ONNX models already integrated in prior milestones
- `Windows.System.MemoryManager` (WinRT)
- DXGI adapter enumeration via P/Invoke or `Microsoft.Windows.Devices.Display.Core` for driver version detection

### Fakes and test doubles

- `FakeBenchmarkScenario` in `tests/BabelStudio.TestDoubles/`: returns a fixed `BenchmarkResult` with configurable RTF and memory values; used for preset recommendation threshold tests
- `FakeHardwareProfiler` in `tests/BabelStudio.TestDoubles/`: returns a configurable `HardwareProfile` with controllable adapter name, driver version, VRAM, and RAM values; used to test driver-change invalidation and runtime planner provider selection without DXGI calls

### Acceptance criteria

- All four benchmark scenarios run and record results.
- RTF computed correctly for ASR and TTS.
- Quality preset recommended with written explanation of which thresholds were met.
- Results stored and not re-run if hardware profile (adapter, driver) has not changed.
- Recommendation overridable; override persisted.
- Runtime planner respects benchmark history when selecting provider.

### Non-goals

- No public leaderboard.
- No telemetry upload.
- No synthetic benchmarks unrelated to actual pipeline models.

### Risks

- Benchmark runtime is long if all four scenarios run sequentially on CPU-only hardware.
- Memory measurement via `AppMemoryUsage` is process-total, not per-model.
- Driver updates (common on Windows) will invalidate cached results frequently.

### Tests

- `FakeBenchmarkScenario` used for all preset recommendation tests.
- Preset thresholds: known RTF and memory values map to correct preset.
- Benchmark persistence round-trip.
- Driver change detection: different adapter description invalidates cached results.
- Runtime planner integration: benchmark result for DirectML causes planner to prefer DirectML.

### Agent tasks

Good agent prompt:

```text
Implement BenchmarkScenario for Whisper ASR: run inference against a 10-second fixture WAV, compute
RTF, record provider used. Store in BenchmarkRuns. Implement RecommendPresetHandler using configurable
RTF thresholds. Add FakeBenchmarkScenario to tests/BabelStudio.TestDoubles/. Test threshold logic
for all four presets.
```

---

## Milestone 20 — Performance profiling and optimization

### Goal

Profile the full pipeline end-to-end and eliminate performance regressions before packaging. Establish memory ceilings and latency targets for minimum-spec hardware.

### Deliverables

Projects:

```
src/BabelStudio.Inference.Onnx/
src/BabelStudio.Infrastructure/
src/BabelStudio.App/
docs/performance/
```

Features:

- ONNX session lifecycle audit: session pooling and reuse across consecutive requests; avoid cold-loading a new session per segment invocation for Whisper, Kokoro, and Opus-MT
- Tensor memory pooling: pre-allocate and reuse input/output tensor buffers for Whisper chunked inference and Demucs chunked processing
- SQLite query profiling: run `EXPLAIN QUERY PLAN` on all repository methods; add indexes where missing; target < 50 ms for any single query on a project with 1,000 segments
- Startup time target: app window visible and interactive within 3 seconds on reference hardware; no model loading or heavy I/O on the startup path
- Background task scheduler audit: verify all long-running operations run on background threads with `CancellationToken` propagation throughout; no single operation blocks the UI thread for > 16 ms
- Memory ceiling test: full pipeline (VAD + ASR + translation + TTS for a 10-minute source) completes within a defined budget; tested on both DirectML (GPU) and CPU paths
- Win2D render budget: waveform strip and timeline maintain ≥ 60 fps on a 2-hour project at all zoom levels; add segment draw-call virtualization if not already in place from M22
- Export pipeline throughput: 30-minute project exports in < 10 minutes on reference DirectML hardware; hardware spec documented in test
- Profiling report: document findings, root causes, and fixes; committed to `docs/performance/profiling-report.md`

### Fakes and test doubles

- `FakeOnnxSessionPool` in `tests/BabelStudio.TestDoubles/`: in-memory session registry; records model path, session open count, and last-used timestamps; configurable cold-load delay; used to verify that real session pool implementations do not re-open a session that is already warm
- SQLite query timing tests use a real on-disk database with a seeded 1,000-segment fixture (not a fake); the fixture is created once per test run and shared across repository timing assertions

### Acceptance criteria

- ONNX sessions pooled: no cold-load measurable per-segment for warm requests to the same model.
- SQLite: no query exceeds 50 ms on a 1,000-segment project with a full artifact and take set.
- Startup: app interactive within 3 seconds measured from process start to first input event.
- Memory: full 10-minute pipeline completes without OOM on 8 GB RAM + 4 GB VRAM minimum spec.
- Win2D: waveform strip renders a 2-hour file at ≥ 60 fps.
- Export: 30-minute project exports within defined wall-clock budget.
- Profiling report committed to `docs/performance/`.

### Non-goals

- No architectural rewrites to improve performance; fixes must be targeted.
- No GPU kernel optimization.
- No distributed or cloud offload.

### Risks

- Session pooling may introduce thread-safety issues if sessions are shared across concurrent requests; guard with a semaphore or per-thread lease.
- Adding SQLite indexes must not degrade write throughput for stage run and artifact inserts.
- Win2D virtualization may require non-trivial view model changes if M22 timeline did not implement it.
- Startup profiling on clean machines may differ substantially from developer machines with warm caches.

### Tests

- ONNX session reuse: invoke engine twice on the same model path; verify cold-load occurs only once, measured via session-open timestamp in `DiagnosticsCollector`.
- SQLite query timing: each repository method's execution time asserted against budget using a seeded 1,000-segment database fixture.
- Startup time: measure time from app start to first `Activated` event; assert ≤ 3 seconds (CI agents may skip).
- Memory ceiling: instrument pipeline run and assert peak `AppMemoryUsage` stays within defined budget.
- Export throughput: 30-minute fixture project export measured and logged to `docs/performance/`.

### Agent tasks

Good agent prompts:

```text
Audit all OnnxInferenceSession creation sites in BabelStudio.Inference.Onnx. Implement a session
pool per model path that reuses loaded sessions across requests. Add a thread-safety guard (semaphore
or reader-writer lease). Add a test verifying no second cold-load occurs on repeated requests to
the same engine instance.
```

```text
Run EXPLAIN QUERY PLAN on all Dapper queries in BabelStudio.Infrastructure repositories. For each
query performing a full table scan on a table that grows with project size (Artifacts, TtsTakes,
TranscriptSegments, TranslatedSegments), add an index to the migration. Add an integration test
asserting query latency on a seeded 1,000-segment database is within budget.
```

---

## Milestone 21 — Model manager and downloads

### Goal

Let users see required models, download missing ones, verify integrity, and manage their model cache through a UI with license visibility.

### Deliverables

Projects:

```
src/BabelStudio.Application/
src/BabelStudio.Infrastructure/
src/BabelStudio.App/
```

Features:

- Model list UI: all pipeline models listed with installed / missing / corrupt / downloading state
- Download queue: sequential downloads with per-model progress; pause and cancel supported
- Hugging Face Hub API integration: resolve model file URLs from `model_id` and `revision` in manifest via `https://huggingface.co/api/`
- SHA-256 verification: hash checked after each download using manifest `sha256` field; corrupt or incomplete files flagged
- License display: model license and `commercial_allowed` status visible before install button is active
- Commercial-safe badge per model
- Delete model: removes files, updates `ModelCache` record
- Repair cache: re-verify all installed models, flag corrupt, offer re-download
- Download interruption handling: partial file deleted on interruption; re-download from start (no resume in this milestone)
- Model size pre-flight: show download size before starting; warn when available disk space is insufficient

### Models and libraries

- Hugging Face Hub REST API via `HttpClient`
- `System.IO.Hashing` / `SHA256.HashData` (.NET built-in)
- No new ONNX models

### Fakes and test doubles

- `FakeModelDownloader` in `tests/BabelStudio.TestDoubles/`: writes a fixture byte sequence to the target path; emits progress events; supports simulated failure and interruption
- `FakeHashVerifier` in `tests/BabelStudio.TestDoubles/`: returns configurable pass/fail per file path; used to test corrupt-file detection and re-download triggering without computing real SHA-256
- `FakeHuggingFaceHubClient` in `tests/BabelStudio.TestDoubles/`: returns configurable model metadata and download URLs for given `model_id`/`revision` pairs; simulates network errors and 404 responses; used in all model manager tests without making real HTTP calls

### Acceptance criteria

- All pipeline models listed with correct state.
- Missing model can be downloaded; hash verified after download.
- SHA-256 mismatch detected; file deleted; user notified.
- License and commercial-safe status visible before install confirmed.
- Delete removes files and cache record.
- Repair re-verifies and flags corrupt models.
- Insufficient disk space warning before download starts.
- Partial file cleaned up on interruption.

### Non-goals

- No model marketplace or plugin system.
- No auto-update without user confirmation.
- No download resume (resume in a future iteration).

### Risks

- Hugging Face network instability will cause download failures; error handling must be clear.
- Model file sizes (MADLAD ~4 GB, Chatterbox ~750 MB) make disk space a real concern.
- License metadata in manifests may become stale as models are updated on Hugging Face.

### Tests

- `FakeModelDownloader` progress events fire correctly.
- Hash mismatch: `FakeHashVerifier` returns fail; file deleted; error reported.
- License display: non-commercial model shows correct badge.
- Insufficient disk space: pre-flight fails before download starts.
- Delete: files removed; cache record updated.
- Repair: corrupt model flagged; re-download queued.

### Agent tasks

Good agent prompt:

```text
Implement ModelCacheService with Hugging Face Hub URL resolver and IModelDownloader interface.
Add FakeModelDownloader and FakeHashVerifier to tests/BabelStudio.TestDoubles/. Implement SHA-256
verification post-download. Add disk space pre-flight check. Test installed/missing/corrupt states,
hash mismatch, and download interruption cleanup.
```

---

## Milestone 22 — Timeline and editor expansion

### Goal

Expand from the transcript list view to a proper editorial workspace with a Win2D waveform timeline, speaker lanes, segment selection, and per-segment re-run commands.

### Deliverables

Projects:

```
src/BabelStudio.App/
```

Features:

- `TimelineControl`: Win2D `CanvasAnimatedControl`-based timeline rendering waveform, speaker lanes, and segment overlays
- Speaker lanes: horizontal lane per speaker; segments colored by TTS state (not generated / generated / stale / cloned)
- Segment selection: click selects segment; inspector panel shows transcript text, translation, TTS take state, duration info
- Transport sync: timeline cursor synced bidirectionally with `PlaybackService`
- Zoom and pan: horizontal zoom via scroll wheel or pinch; pan via drag on timeline
- Per-word alignment overlay: where Whisper word-level timestamps are available, render word boundaries within a segment
- Stale and missing indicators per segment
- Commands on selected segment: re-transcribe, re-translate, re-TTS, re-stretch, open in split editor
- Keyboard shortcuts: Space (play/pause), J/K/L (rewind/play/fast-forward at 1×/1×/2×), Left/Right arrow (previous/next segment)
- Segment virtualization: only render segments visible in the current viewport; required for projects with thousands of segments

### Models and libraries

- `Microsoft.Graphics.Canvas` (Win2D) — `CanvasAnimatedControl`, `CanvasDrawingSession`
- No new ONNX models

### Fakes and test doubles

- `FakeTtsTakeRepository` in `tests/BabelStudio.TestDoubles/`: in-memory collection of `TtsTake` records; configurable initial state including stale flags and take kind; used in timeline view model tests to drive segment color-state and command availability
- `FakeSpeakerRepository` in `tests/BabelStudio.TestDoubles/`: in-memory collection of `Speaker` records and lane assignments; used in timeline speaker-lane rendering and speaker color tests
- `TimelineViewModel` tests use `FakeTtsTakeRepository`, `FakeSpeakerRepository`, and the existing fake transcript/translation repositories; no Win2D rendering in tests
- Command availability tests do not require a real canvas

### Acceptance criteria

- Timeline renders waveform and segments for a real project without performance regression.
- Clicking a segment selects it and seeks the video player.
- Transport cursor moves with playback.
- Zoom and pan work at all zoom levels.
- Stale and missing segments visually distinct from current.
- Per-segment commands available and functional.
- Keyboard shortcuts work.
- Segment virtualization: 1,000-segment project renders without frame drop.

### Non-goals

- No full nonlinear video editor.
- No keyframe animation.
- No arbitrary clip editing outside segment boundaries.

### Risks

- Win2D `CanvasAnimatedControl` GPU resource contention on long projects.
- Timeline/`PlaybackService` sync introducing visible latency.
- Segment virtualization complexity for the zoom and pan case.

### Tests

- `TimelineViewModel`: correct segment positions; correct stale state from fake repositories.
- Command availability: re-TTS enabled only when translated text exists; re-stretch enabled only when take exists.
- Selection state: selecting a segment updates inspector panel view model.
- Keyboard shortcut handling on view model (no UI required).

### Agent tasks

Good agent prompt:

```text
Implement TimelineViewModel with segment positions, speaker lane assignment, stale indicators, and
selection state, driven by fake repositories. Add commands for re-transcribe/re-translate/re-TTS/
re-stretch on selected segment. Add keyboard shortcut handling. Test selection, stale indicators,
and command availability. Do not implement Win2D canvas rendering in this task.
```

---

## Milestone 23 — UX and accessibility polish

### Goal

Ensure the full UI surface meets accessibility standards, handles all error and empty states gracefully, and provides consistent visual feedback across all long-running operations. This milestone runs before packaging — it is much cheaper to find and fix UX gaps before a clean-machine install test than after.

### Deliverables

Projects:

```
src/BabelStudio.App/
```

Features:

- Keyboard navigation audit: complete and correct tab order for all views; no mouse-only interactive flows; every button, dropdown, list, and custom control reachable by keyboard
- Screen reader support: `AutomationProperties.Name` on all interactive and informational controls; landmark regions for main panels (transcript list, translation panel, video player, timeline); stage completion and error events announced via `AutomationPeer`
- WCAG 2.1 AA contrast: all text and interactive elements meet 4.5:1 minimum contrast ratio in both light and dark Windows themes; no hardcoded color values that bypass the theme resource system
- Windows theme support: app fully respects Windows light/dark system theme switching at runtime; all controls use WinUI 3 theme resources exclusively
- Loading and progress consistency: every operation taking > 500 ms exposes a progress indicator with a cancel button; no spinner without a cancel path; no operation blocks the UI thread
- Empty states: first launch with no projects, transcript not yet generated, translation not yet run, no models installed — each panel shows informative content and a clear next action rather than a blank area
- Error message quality pass: audit all user-visible error strings; no raw exception messages, HRESULTs, or stack traces surfaced to the user; every error maps to a plain-language explanation with an actionable next step
- Form validation: inline validation on all constrained input fields (segment time fields, loudness LUFS target, glossary entries); fires on focus-lost, not on keystroke; valid input clears the message
- Keyboard shortcut discoverability: a keyboard shortcut reference panel accessible from the Help menu or via a `?` shortcut
- Onboarding review: first-run wizard copy reviewed for clarity; tooltip and hint coverage for non-obvious controls (stale indicators, runtime planner output, commercial-safe mode toggle)
- High DPI and display scaling: layout integrity verified at 100%, 125%, 150%, and 200% Windows display scaling

### Models and libraries

- WinUI 3 `AutomationProperties` (inbox)
- `Windows.UI.ViewManagement.AccessibilitySettings` for high-contrast detection
- No new ONNX models

### Fakes and test doubles

- Keyboard navigation and automation tests traverse the `AutomationPeer` tree; no visual rendering required
- Empty state tests use fake repositories returning empty collections
- `IsBusy` tests use fake async commands on view models

### Acceptance criteria

- All interactive controls have non-empty `AutomationProperties.Name`.
- Tab order is logical and complete across all main views.
- All text meets 4.5:1 contrast in both light and dark themes.
- Every async command exposes `IsBusy` and a cancel path.
- All defined empty states render with informative content and a next action.
- No raw exception text reachable through a normal user workflow.
- Form validation fires on focus-lost with correct inline messages; valid input clears them.
- Keyboard shortcut reference panel accessible and complete.
- Layout intact at 100%, 125%, 150%, and 200% DPI scaling.

### Non-goals

- No custom assistive technology integration beyond standard WinUI 3 `AutomationPeer`.
- No right-to-left layout support in this milestone.
- No localization (UI strings remain English-only until a future milestone).
- No redesign of visual style; this is a compliance and consistency pass only.

### Risks

- WinUI 3 `AutomationPeer` support is inconsistent for custom Win2D canvas controls; `TimelineControl` from M22 will require a manual peer implementation.
- High-contrast theme testing may reveal hardcoded colors not caught by light/dark testing.
- Retroactively adding cancel paths to operations that lack `CancellationToken` propagation may require non-trivial refactoring of stage handlers.

### Tests

- `AutomationPeer` tree test: enumerate all focusable elements in main views; assert none have an empty `AutomationProperties.Name`.
- Empty state test: each panel's view model with a fake empty repository asserts that the empty state flag is true and the error/content flags are false.
- `IsBusy` test: all async commands on view models expose `IsBusy = true` during execution and `false` after completion or cancellation.
- Form validation test: segment time field with invalid input sets a validation message on focus-lost; valid value clears it.
- High DPI manual test: app launched at each scale factor; no clipped controls or overlapping elements.

### Agent tasks

Good agent prompts:

```text
Audit all view models in BabelStudio.App for async commands that do not expose an IsBusy observable
or do not propagate a CancellationToken. Add IsBusy and a CancellationTokenSource-backed cancel
command to each. Add tests verifying IsBusy is true during execution and false after completion or
cancellation.
```

```text
Add AutomationProperties.Name to all interactive and informational controls in MainWindow.xaml and
all child views. Define empty state DataTemplates for the transcript list, translation panel, TTS
panel, and project home screen. Add AutomationPeer tree tests asserting no focusable element has
an empty name.
```

---

## Milestone 24 — Commercial-safe mode and licensing UI

### Goal

Make model and provider license safety visible, enforceable at runtime, and auditable at export.

### Deliverables

Projects:

```
src/BabelStudio.Application/
src/BabelStudio.App/
```

Features:

- Commercial-safe mode toggle in settings (persisted via `SettingsService`, set in M8)
- `CommercialSafeModeService`: enforces model selection at all pipeline entry points; blocks models where `commercial_allowed` is false or license is unknown
- User-facing explanation when a model is blocked: which manifest field failed, what the declared license is
- Model license panel: all models in cache listed with license, `commercial_allowed`, `requires_attribution`, `voice_cloning` flags
- Export license manifest: JSON alongside export listing all models used, licenses, and attribution requirements
- Third-party notices generator: `THIRD_PARTY_NOTICES.txt` generated per export from attribution-required models
- Voice cloning warning: visible in UI whenever voice cloning is active, regardless of commercial-safe mode state

### Fakes and test doubles

- `FakeModelManifestRepository` in `tests/BabelStudio.TestDoubles/`: in-memory collection of `ModelManifest` records with configurable `commercial_allowed`, `license`, `requires_attribution`, and `voice_cloning` fields; used in all `CommercialSafeModeService` and export manifest tests without needing real manifest files on disk

### Acceptance criteria

- Unsafe models blocked and explained in commercial-safe mode.
- Attribution-required models listed in export manifest.
- Third-party notices generated for any export.
- Commercial-safe toggle persists and respected on next launch.
- Voice cloning warning visible regardless of mode.

### Non-goals

- No legal advice engine.
- No automatic rights clearance.
- No guarantee every use case is legally safe.

### Risks

- Users interpret "commercial safe" as legal clearance — UI language must be careful.
- License metadata in manifests may become stale independently of model updates.
- Provider terms (Windows ML, DirectML) vary from model licenses and are not covered by `commercial_allowed`.

### Tests

- Commercial-safe blocking: unknown-license model blocked; non-commercial model blocked.
- Attribution-required model included in export manifest.
- Third-party notices content: attribution-required models listed.
- Toggle persistence: enabled on relaunch.
- Voice cloning warning: present even when commercial-safe mode is off.

### Agent tasks

Good agent prompt:

```text
Implement CommercialSafeModeService and ExportLicenseManifestWriter. Block models with unknown or
non-commercial licenses in commercial-safe mode. Include attribution-required models in export
manifest. Generate THIRD_PARTY_NOTICES.txt. Add tests for each blocking condition and manifest content.
```

---

## Milestone 25 — Packaging and clean-machine install

### Goal

Make Babel Studio install and run on a clean Windows machine without developer tools.

### Deliverables

Features:

- Signed installer: MSIX package or WiX Toolset v4 MSI
- Windows App SDK bootstrapper configuration for unpackaged deployment (`WindowsPackageType: None`, `WindowsAppSDKSelfContained: true`)
- FFmpeg static build bundled: LGPL-licensed build from gyan.dev/ffmpeg/builds; license disclosure in installer
- espeak-ng Win32 build bundled: required for Kokoro G2P phonemizer path
- First-run setup wizard: hardware profiler prompt, model download guidance, commercial-safe mode explanation, codec warning if relevant
- Clean uninstall: removes app binaries and `%LocalAppData%/BabelStudio/` (user prompted before data deletion)
- Repair tool: re-verifies model cache and re-downloads corrupt models
- Diagnostics bundle accessible from installer error screen

### Models and libraries

- WiX Toolset v4 or MSIX packaging via Visual Studio
- Windows App SDK bootstrapper
- FFmpeg LGPL static build (gyan.dev)
- espeak-ng Win32 build

### Acceptance criteria

- Fresh Windows 11 install runs without Visual Studio, .NET SDK, Python, or CUDA Toolkit.
- Installer includes FFmpeg and espeak-ng with license disclosure.
- First-run wizard completes without error on clean machine.
- App can create a project and run the full transcript slice.
- Clean uninstall leaves no application files.
- Diagnostics bundle accessible from installer error screen.

### Non-goals

- No auto-updater in this milestone.
- No enterprise deployment tooling.
- No Windows 10 support assessment.

### Risks

- Windows App SDK unpackaged bootstrapper has known edge cases with some Windows configurations.
- Antivirus false positives on ONNX Runtime DLLs are common.
- FFmpeg LGPL compliance requires dynamic linking or source offer — verify build configuration.
- Model downloads during first run may be very large depending on user's selected preset.

### Tests

- Clean VM install test: Windows 11 fresh image, no developer tools.
- Uninstall/reinstall: no residual files after uninstall.
- First-run failure: intentionally missing model → first-run wizard handles gracefully.

### Agent tasks

Good agent prompt:

```text
Create packaging checklist and installer smoke script. Identify all runtime dependencies (Windows App
SDK, ONNX Runtime DLLs, DirectML DLLs, FFmpeg, espeak-ng) required for clean-machine run. Verify
FFmpeg build is LGPL-compliant. Do not alter application code.
```

---

## Milestone 26 — Public alpha

### Goal

Release a limited, honest alpha for technical users.

### Minimum alpha capability

- Open local media
- Create and reopen project
- Extract audio
- Run transcript stage (real Silero VAD + Whisper Large-V3-Turbo)
- Edit transcript, including segment split/merge
- Run translation stage (real Opus-MT / MADLAD-400)
- Run stock TTS stage (real Kokoro-82M)
- TTS timing reconciliation
- Preview dubbed segment or range in video player
- Export dubbed audio/video with subtitle sidecar
- Diagnostics bundle
- Model/license visibility

### Alpha messaging

```
Babel Studio is an early local-first dubbing workstation.
Expect rough edges.
Do not use it for production or commercial work without independently validating model licenses
and output quality.
Voice cloning is opt-in and requires explicit consent each session.
```

### Non-goals

- No broad consumer marketing.
- No paid plans yet.
- No voice cloning as a headline feature.

### Risks

- Users expect a finished product.
- Model downloads are very large on first run.
- Runtime failures underdiagnosed without diagnostics export.
- Support burden spikes unexpectedly.

### Tests

- Public release smoke script: create project, ingest media, run transcript, translate, TTS, export.
- Clean install test on at least two hardware configurations: discrete NVIDIA GPU and CPU-only.
- Project reopen: all data present after close.
- Diagnostics export: bundle produced and contains expected files.
- Common failure paths: missing model, unsupported codec, inference failure — all produce clear messages.

---

## Milestone 27 — Beta and monetization readiness

### Goal

Prepare for serious user adoption and optional monetization.

### Required before monetization

- Stable installer
- Stable project persistence
- Export works
- Diagnostics works
- Commercial-safe mode works
- All included model licenses audited and manifests current
- CLA process in place for contributors
- Support channel established
- Privacy policy published
- Terms of use and commercial license draft complete

### Monetization approach

Recommended:

```
Free GPL community edition
GitHub Sponsors / Ko-fi for donations
Commercial license for non-GPL organizational use
Optional paid cloud credits later
Support contracts for businesses
```

Do not monetize:

- Save/reopen
- Export
- Diagnostics
- License visibility
- Privacy controls
- Basic local transcript path

### Acceptance criteria

- Clear public license posture documented.
- Commercial license draft in `COMMERCIAL-LICENSE.md`.
- CLA process documented and functional.
- Model license manifest complete for all included models.
- Public docs explain limitations and alpha status honestly.

---

## Revised dependency map

```
M0  Repo foundation
  ↓
M1  Runtime harness
  ↓
M2  Model manifest / license policy
  ↓
M3  SQLite project spine
  ↓
M4  Media ingest / artifact store
  ↓
M5  Runtime planner / model cache
  ↓
M6  Transcript vertical slice   (real Silero VAD + Whisper Large-V3-Turbo ONNX)
  ↓
M7  Translation slice           (real direct Opus-MT EN <-> ES)
  ↓
M8  Video player, segment editor, settings, project management
  ↓
M9  Expanded translation routing (Opus-MT direct + MADLAD-400 pivot, broader language router)
  ↓
M10 Speaker diarization         (SortFormer ONNX)
  ↓
M11 Stock voice TTS             (Kokoro-82M ONNX + espeak-ng)
  ↓
M12 TTS timing reconciliation   (FFmpeg atempo)
  ↓
M13 Stem separation             (Demucs htdemucs ONNX)
  ↓
M14 Preview mix
  ↓
M15 Export                      (subtitles + loudness normalization)
  ↓
M16 Glossary and terminology
  ↓
M17 Voice cloning               (Chatterbox ONNX)
  ↓
M18 Diagnostics bundle
  ↓
M19 Hardware profiler           (real ONNX benchmark scenarios)
  ↓
M20 Performance profiling and optimization
  ↓
M21 Model manager and downloads (Hugging Face Hub API)
  ↓
M22 Timeline and editor expansion (Win2D CanvasAnimatedControl)
  ↓
M23 UX and accessibility polish
  ↓
M24 Commercial-safe mode and licensing UI
  ↓
M25 Packaging and clean-machine install
  ↓
M26 Public alpha
  ↓
M27 Beta and monetization readiness
```

Milestones that can begin earlier once prerequisites are met:

```
M18 Diagnostics can begin after M6 (logging infrastructure exists).
M19 Hardware profiler can begin after M9 (real models needed for meaningful RTF).
M20 Performance profiling can begin after M15 (full pipeline must be running for meaningful measurement).
M21 Model manager can begin after M2 and M5.
M24 Licensing UI can begin after M2.
M25 Packaging can begin after M8.
```

---

## Final rule — unchanged

Do not chase the impressive feature first.

```
runtime proof       M1
state proof         M3
media proof         M4
transcript proof    M6
video player        M8
translation proof   M9
TTS proof           M11
timing proof        M12
preview proof       M14
export proof        M15
then voice cloning  M17
```

Babel Studio becomes valuable when the pipeline is reliable enough that users trust it. Architecture and milestones must protect that trust from day one.

---

## Complete fakes inventory

All fakes live in `tests/BabelStudio.TestDoubles/`. Every interface with an ONNX-backed or I/O-bound real implementation must have a corresponding fake before its milestone is considered complete.

| Fake | Interface | Introduced | Purpose |
|---|---|---|---|
| `FakeExecutionProviderDiscovery` | `IExecutionProviderDiscovery` | M5 | Configurable provider availability; no DXGI or system calls |
| `FakeModelCache` | `IModelCache` | M5 | Configurable installed/missing/corrupt state per model ID; no disk access |
| `FakeVadEngine` | `IVoiceActivityDetector` | M6 | Returns hard-coded speech intervals from fixture JSON; no I/O |
| `FakeAsrEngine` | `ITranscriptionEngine` | M6 | Returns deterministic `TranscriptSegment` list; no ONNX session |
| `FakeTranslationEngine` | `ITranslationEngine` | M7 | Returns `[TRANSLATED]`-prefixed strings; applies glossary hints via string substitution (extended M9) |
| `FakeMediaPlayer` | `IMediaPlayer` | M8 | In-memory position tracking; no media file access |
| `FakeSettingsService` | `ISettingsService` | M8 | In-memory settings store; no filesystem access |
| `FakeRecentProjectsRepository` | `IRecentProjectsRepository` | M8 | In-memory recent project list; configurable state |
| `FakeTranslationLanguageRouter` | `ITranslationLanguageRouter` | M9 | Configurable supported pair set and routing path (direct or pivot) |
| `FakeDiarizationEngine` | `IDiarizationEngine` | M10 | Returns hard-coded speaker turns from a fixture JSON |
| `FakeTtsEngine` | `ITtsEngine` | M11 | Returns tiny silent WAV; exposes `LastInputText`, `LastVoicepack` |
| `FakePhonemizer` | `IGraphemeToPhoneme` | M11 | Returns fixed phoneme string; no espeak-ng subprocess |
| `FakeVoiceCatalog` | `IVoiceCatalog` | M11 | Configurable voicepack list by language and gender |
| `FakeAudioTimeStretchService` | `IAudioTimeStretchService` | M12 | Records stretch ratio; returns input WAV unchanged |
| `FakeStemSeparationEngine` | `IStemSeparationEngine` | M13 | Writes input WAV as both vocals and instrumental; no inference |
| `FakeMixRenderer` | `IMixRenderer` | M14 | Returns fixed-length silent WAV; records calls |
| `FakeArtifactStore` | `IArtifactStore` | M14 | In-memory artifact registry; no disk writes; configurable pre-seeded paths |
| `FakeExportRenderer` | `IExportRenderer` | M15 | Records `ExportPlan`; writes tiny fixture MP4 |
| `FakeLoudnessNormalizer` | `ILoudnessNormalizer` | M15 | Records LUFS target; returns input path unchanged |
| `FakeGlossaryRepository` | `IGlossaryRepository` | M16 | In-memory glossary entries; project and global scope |
| `FakeVoiceCloneTtsEngine` | `IVoiceCloneTtsEngine` | M17 | Extends `FakeTtsEngine`; throws `ConsentRequiredException` without consent |
| `FakeConsentService` | `IConsentService` | M17 | In-memory per-session consent state; no persistence |
| `FakeDiagnosticsCollector` | `IDiagnosticsCollector` | M18 | Returns fixed `DiagnosticsSnapshot` with known values |
| `FakeAppHealthMonitor` | `IAppHealthMonitor` | M18 | In-memory stage health state; configurable summary |
| `FakeBenchmarkScenario` | `IBenchmarkScenario` | M19 | Returns fixed `BenchmarkResult` with configurable RTF and memory values |
| `FakeHardwareProfiler` | `IHardwareProfiler` | M19 | Returns configurable `HardwareProfile`; no DXGI calls |
| `FakeOnnxSessionPool` | `IOnnxSessionPool` | M20 | In-memory session registry; tracks open count and timestamps |
| `FakeModelDownloader` | `IModelDownloader` | M21 | Writes fixture bytes; emits progress; supports simulated failure |
| `FakeHashVerifier` | `IHashVerifier` | M21 | Configurable pass/fail per file path; no real SHA-256 |
| `FakeHuggingFaceHubClient` | `IHuggingFaceHubClient` | M21 | Returns configurable metadata and URLs; simulates network errors |
| `FakeTtsTakeRepository` | `ITtsTakeRepository` | M22 | In-memory take records with configurable stale flags and take kind |
| `FakeSpeakerRepository` | `ISpeakerRepository` | M22 | In-memory speaker and lane records |
| `FakeModelManifestRepository` | `IModelManifestRepository` | M24 | In-memory manifests with configurable license and flag fields |
