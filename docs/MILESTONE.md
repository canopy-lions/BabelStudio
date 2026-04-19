# MILESTONE.md

# Babel Studio Milestone Plan

Babel Studio is a Windows-native, local-first AI dubbing workstation. The project should be built in thin, verifiable slices rather than as one giant application pass.

The main rule for this plan:

> Prove the runtime and persistence spine before building the full editor.

The product is ambitious enough that the biggest risk is not UI polish. The biggest risks are model/runtime viability, artifact integrity, media sync, licensing, and avoiding false readiness.

---

## Milestone philosophy

Each milestone should produce one of these:

1. A technical proof that reduces major uncertainty.
2. A durable product foundation.
3. A vertical slice users can actually exercise.
4. A safety/legal/commercial foundation that prevents future rework.

Each milestone should have:

- a goal
- acceptance criteria
- non-goals
- risks
- agent task boundaries
- tests or validation steps

Do not move to feature expansion until the previous milestone has a boring, repeatable success path.

---

## Milestone 0 — Repository and architecture foundation

### Goal

Create the repo foundation, document architectural boundaries, and prevent coding agents from turning the project into a soup cauldron.

### Deliverables

- Root `README.md`
- `ARCHITECTURE.md`
- `MILESTONE.md`
- `AGENTS.md`
- `LICENSE`
- `COMMERCIAL-LICENSE.md`
- `CONTRIBUTOR-LICENSE-AGREEMENT.md`
- `MODEL_LICENSE_POLICY.md`
- `THIRD_PARTY_NOTICES.md`
- `.gitignore`
- `Directory.Build.props`
- `Directory.Packages.props`
- starter directory layout
- README files in important directories

### Acceptance criteria

- Repo clearly states this is early architecture/prototype work.
- GPLv3 + commercial dual-license intent is documented.
- Agent guardrails exist.
- Model license policy exists before any model is added.
- Directory boundaries are clear enough that Codex/Claude Code can be given scoped tasks.

### Non-goals

- No app UI.
- No model execution.
- No installer.
- No cloud provider work.
- No monetization implementation.

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

---

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

- Load a single ONNX model from disk.
- Select or report execution provider.
- Run cold-load measurement.
- Run warm inference measurement.
- Measure wall-clock latency.
- Measure real-time factor where applicable.
- Record success/failure.
- Emit JSON report.
- Emit console summary.

Initial target models:

1. Silero VAD ONNX
2. one Whisper ONNX export
3. one Opus-MT ONNX pair
4. one stock TTS ONNX candidate
5. optional Chatterbox ONNX spike

### Acceptance criteria

- Harness runs from command line.
- At least one model loads and runs from C#.
- Harness reports provider, load time, inference time, and failure reason.
- Failed model/provider combinations are reported clearly, not swallowed.
- No UI project exists yet.

### Non-goals

- No WinUI.
- No project database.
- No video ingest.
- No full pipeline.
- No voice cloning UX.

### Risks

- Windows ML / ONNX provider availability differs across machines.
- Some ONNX exports may require unsupported ops or custom preprocessing.
- Tokenizer/model glue may be more complex than model loading.
- TTS models may be much heavier than expected.

### Tests

- Unit tests for result formatting.
- Smoke test for one tiny ONNX model.
- Harness regression test using a local fixture model if available.
- Golden report format test.

### Agent tasks

Good agent prompt:

```text
Implement BabelStudio.Benchmarks as a .NET console app that loads a supplied ONNX model path, runs one inference call with dummy or fixture input, reports provider, cold load time, warm latency, and writes benchmark-report.json. Do not create UI.
```

---

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

- `ModelManifest` type
- manifest JSON schema
- model task enum
- license metadata
- commercial-safe evaluator
- local model cache record
- hash verification policy
- model variant metadata
- required consent flags
- attribution flags

Example manifest fields:

```json
{
  "model_id": "example/model",
  "task": "asr",
  "license": "MIT",
  "commercial_allowed": true,
  "redistribution_allowed": true,
  "requires_attribution": false,
  "requires_user_consent": false,
  "voice_cloning": false,
  "commercial_safe_mode": true,
  "source_url": "",
  "revision": "",
  "sha256": "",
  "variants": []
}
```

### Acceptance criteria

- Unknown-license models are not commercial-safe.
- Non-commercial models are not commercial-safe.
- Voice-cloning models require consent.
- Manifests can be loaded and validated.
- Invalid manifests produce useful errors.
- Tests cover commercial-safe logic.

### Non-goals

- No model downloading yet.
- No UI model manager yet.
- No legal finalization.
- No automatic Hugging Face lookup yet.

### Risks

- License metadata can become stale.
- Model cards vary in clarity.
- Exported ONNX models may have different terms than source repos.

### Tests

- Valid manifest test.
- Invalid license test.
- Non-commercial exclusion test.
- Voice-cloning consent-required test.
- Attribution-required test.

### Agent tasks

Good agent prompt:

```text
Implement ModelManifest parsing and CommercialSafeEvaluator. Add tests proving unknown and non-commercial licenses are rejected in commercial-safe mode. Do not add real model downloads.
```

---

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

- database migrator
- connection factory
- project repository
- artifact repository
- stage run repository
- model cache repository
- benchmark repository

### Acceptance criteria

- Database can be created from scratch.
- Migrations apply in order.
- Project can be created.
- Stage run can be created and completed.
- Artifact can be registered with hash/path/kind/provenance.
- Project can be reopened.
- Tests use temporary SQLite files.

### Non-goals

- No UI.
- No media extraction yet.
- No real ONNX inference.
- No export.

### Risks

- Schema gets too broad too early.
- Missing artifact provenance causes rework later.
- Destructive migrations create project corruption risk.

### Tests

- Migration test.
- Project repository test.
- StageRun repository test.
- Artifact repository test.
- Transaction rollback test.
- Schema version test.

### Agent tasks

Good agent prompt:

```text
Implement SQLite migration runner and repositories for Projects, StageRuns, Artifacts, and ModelCache using Dapper. Tests must use temporary database files. Do not create UI.
```

---

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

- media probe abstraction
- FFmpeg/ffprobe wrapper
- normalized audio extraction
- source media fingerprinting
- waveform summary generation
- artifact folder layout
- atomic artifact writes
- media asset repository integration

Project artifact layout:

```text
ProjectName.babelstudio/
├── babel.db
├── manifest.json
├── media/
│   ├── source-reference.json
│   └── normalized_audio.wav
├── artifacts/
│   ├── audio/
│   └── waveform/
├── logs/
└── temp/
```

### Acceptance criteria

- Local video/audio can be probed.
- Source media metadata is stored.
- Working audio is extracted.
- Audio artifact is registered with hash and duration.
- Waveform summary is generated and registered.
- Project can be reopened and artifacts found.
- Missing source file is reported clearly.

### Non-goals

- No video editor UI.
- No ASR yet.
- No final export.
- No stem separation.

### Risks

- FFmpeg availability and licensing.
- Weird media containers.
- Variable frame rate and stream start offsets.
- Long path issues on Windows.

### Tests

- Probe fixture media.
- Extract fixture audio.
- Hash verification.
- Missing media recovery.
- Artifact atomic write test.

### Agent tasks

Good agent prompt:

```text
Implement MediaProbe and AudioExtractionService using FFmpeg process execution. Store outputs through IArtifactStore. Add tests using tiny sample media. Do not add ASR.
```

---

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

- hardware profile
- execution provider discovery abstraction
- model variant selector
- model cache records
- runtime plan record
- stage runtime requirements
- smoke-test interface
- fallback explanation

Example runtime plan:

```json
{
  "stage": "ASR",
  "model_id": "whisper-large-v3-turbo",
  "variant": "fp16",
  "execution_provider": "DirectML",
  "fallback_reason": null,
  "warnings": []
}
```

### Acceptance criteria

- Planner can choose between GPU and CPU variants.
- Planner explains fallback reasons.
- Missing model produces a download-needed state.
- Commercial-safe mode affects model selection.
- Runtime plan is serializable for logs/stage runs.

### Non-goals

- No actual model download UI.
- No full benchmark-based preset yet.
- No cloud providers.

### Risks

- Planner becomes speculative and too complex.
- Provider availability is confused with model compatibility.
- Users see “GPU ready” before a real model passes.

### Tests

- GPU available plan test.
- CPU fallback plan test.
- missing model test.
- commercial-safe exclusion test.
- provider smoke-test failure test.

### Agent tasks

Good agent prompt:

```text
Implement RuntimePlanner using fake hardware/model providers. It should produce a StageRuntimePlan with fallback explanations and commercial-safe filtering. Add tests. Do not load real ONNX models.
```

---

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

- minimal WinUI shell
- open media command
- project creation
- media ingest
- VAD stage
- ASR stage
- transcript segment storage
- transcript list UI
- manual transcript editing
- stage status display
- reopen project with transcript

### Acceptance criteria

- User can open a media file.
- App creates project.
- App extracts audio.
- App runs VAD/ASR through validated path or test/fake engine.
- Transcript segments appear.
- User can edit transcript text.
- Edits persist.
- Project reopens without recomputing.
- Stage run and artifact provenance are stored.

### Non-goals

- No translation.
- No TTS.
- No voice cloning.
- No fancy timeline.
- No final export.

### Risks

- UI is built too wide too early.
- ASR wrapper not stable.
- Transcript edits overwrite generated provenance.
- Long-running tasks block UI.

### Tests

- Application use case tests with fake ASR.
- Transcript persistence test.
- Stage-run commit test.
- UI smoke test if practical.
- Manual end-to-end test on tiny media.

### Agent tasks

Good agent prompt:

```text
Build a minimal WinUI transcript slice: open existing project, display TranscriptSegments from repository, allow editing text, persist edits. Use fake data first. Do not add translation or TTS.
```

---

## Milestone 7 — Translation slice

### Goal

Translate transcript segments and persist editable translation revisions.

### Deliverables

Features:

- target language selection
- translation runtime plan
- translation model resolver
- translation stage run
- translated segment storage
- translation editor
- stale marker when transcript changes
- commercial-safe model filtering

### Acceptance criteria

- User can translate transcript segments.
- Translation results are stored as a revision.
- User can edit translated text.
- Transcript edit marks affected translation stale.
- Reopen preserves translation.
- Model/provider used is recorded.

### Non-goals

- No TTS.
- No multi-language full automation unless direct.
- No cloud providers.
- No glossary yet.

### Risks

- Opus-MT pair availability gaps.
- Tokenizer implementation complexity.
- Pivot translation quality issues.
- Users expect perfect translation.

### Tests

- Translation use case with fake translator.
- Stale marker test.
- Provider metadata test.
- Commercial-safe filtering test.

### Agent tasks

Good agent prompt:

```text
Implement TranslationRevision and TranslatedSegment persistence. Add StartTranslationStageHandler using ITranslationEngine fake. Mark translation stale when source transcript segment changes.
```

---

## Milestone 8 — Stock voice TTS slice

### Goal

Generate dubbed speech using stock voices before attempting voice cloning.

### Deliverables

Features:

- voice catalog
- speaker-to-stock-voice assignment
- TTS runtime plan
- per-segment TTS generation
- TTS take storage
- audio artifact registration
- basic playback of generated take
- stale marker when translated text or voice assignment changes

### Acceptance criteria

- User can select a stock voice.
- User can generate TTS for translated segments.
- TTS audio artifacts are stored.
- User can audition a segment.
- Reopen preserves takes.
- Translation edit marks affected takes stale.
- Voice assignment change marks affected takes stale.

### Non-goals

- No voice cloning yet.
- No full dubbed mix.
- No timing stretch yet.
- No final export.

### Risks

- TTS model quality.
- TTS model runtime size.
- Audio format inconsistencies.
- Segment text too long for original timing.

### Tests

- Fake TTS use case.
- TTS artifact persistence.
- Stale marker tests.
- Audio duration metadata test.

### Agent tasks

Good agent prompt:

```text
Implement TtsTake persistence and StartTtsStageHandler using a fake ITtsEngine that writes short WAV fixtures. Add stale invalidation tests for translated text and voice assignment changes.
```

---

## Milestone 9 — Preview mix slice

### Goal

Preview generated dubbed speech against the original media in context.

### Deliverables

Features:

- mix plan domain model
- source/original audio lane
- dubbed speech lane
- simple gain controls
- preview range render
- playback of range with video
- warnings for missing/stale TTS

### Acceptance criteria

- User can preview a short range.
- Generated TTS aligns to segment start times.
- Original audio can be ducked or muted.
- Preview uses the same MixPlan representation intended for export.
- Stale/missing segments are visible.

### Non-goals

- No final export yet.
- No advanced DAW controls.
- No per-word alignment.
- No perfect dialogue removal.

### Risks

- Preview/export drift.
- Timing representation bugs.
- Audio gaps or overlaps.
- Media playback format issues.

### Tests

- Mix plan construction.
- Range render with fake clips.
- Timing conversion tests.
- Missing/stale clip warning tests.

### Agent tasks

Good agent prompt:

```text
Implement MixPlan and PreviewRangeRenderer using existing WAV artifacts. It should place clips by segment start time and produce a preview WAV for a selected range. Do not build full export.
```

---

## Milestone 10 — Export slice

### Goal

Export a usable dubbed output.

### Deliverables

Features:

- full mix render
- mux output with source video
- optional subtitle export
- export metadata
- export manifest
- output verification

### Acceptance criteria

- User can export final dubbed audio/video.
- Video stream is copied when possible.
- Output duration is checked.
- Export metadata indicates AI-dubbed output where appropriate.
- Export manifest records source project, stages, models, and warnings.
- Export fails with clear reasons.

### Non-goals

- No cloud render.
- No advanced codec UI.
- No multi-version render queue.

### Risks

- FFmpeg command complexity.
- Container compatibility.
- A/V sync drift.
- Licensing around bundled FFmpeg builds.

### Tests

- Mux command builder tests.
- Export manifest tests.
- Tiny video export smoke test.
- Missing source recovery test.

### Agent tasks

Good agent prompt:

```text
Implement ExportPlan and MuxWriter for a source video plus final audio WAV. Preserve video stream where possible and write an export manifest. Add command-builder tests.
```

---

## Milestone 11 — Diarization and speaker workflow

### Goal

Add speaker detection and editable speaker assignment.

### Deliverables

Features:

- diarization engine abstraction
- speaker turn persistence
- speaker panel
- merge/split/rename speakers
- assign voice per speaker
- reference clip candidate extraction
- diarization confidence/warnings

### Acceptance criteria

- Diarization can produce speaker turns.
- User can rename speakers.
- User can merge/split speakers.
- Speaker assignment affects TTS routing.
- Diarization output is editable and not treated as truth.
- Single-speaker/manual workflow still works.

### Non-goals

- No mandatory voice cloning.
- No promise of identifying real people.
- No hard block on diarization failure.

### Risks

- Diarization model license/terms.
- More than supported number of speakers.
- Overlapping speech.
- Noisy source material.

### Tests

- Fake diarization use case.
- Merge/split/rename tests.
- Speaker assignment invalidation tests.
- Reference clip extraction tests.

### Agent tasks

Good agent prompt:

```text
Implement speaker turn persistence and speaker merge/rename use cases. Use fake diarization data. Do not add real SortFormer wrapper yet.
```

---

## Milestone 12 — Optional stem separation

### Goal

Add vocal/instrumental stem separation as an optional quality step.

### Deliverables

Features:

- stem separation engine abstraction
- Demucs/other ONNX wrapper spike
- vocals artifact
- instrumental artifact
- stage warnings
- option to bypass separation
- mix uses instrumental where available

### Acceptance criteria

- User can run or skip stem separation.
- Vocals/instrumental artifacts are stored.
- ASR can use vocals if available.
- Mix can use instrumental if available.
- UI makes it clear stems are estimates.

### Non-goals

- No promise of clean dialogue removal.
- No mandatory stem separation.
- No advanced source separation UI.

### Risks

- Model size and memory.
- Separation artifacts.
- Slow CPU fallback.
- Poor results on some media.

### Tests

- Fake separation test.
- Artifact registration test.
- bypass path test.
- mix path with instrumental test.

### Agent tasks

Good agent prompt:

```text
Add StemSeparationStage with fake IStemSeparationEngine. It should produce vocals/instrumental artifact records and allow bypass. Do not require it for transcript pipeline.
```

---

## Milestone 13 — Voice cloning experiment

### Goal

Add opt-in, consent-gated voice cloning after stock voice TTS is stable.

### Deliverables

Features:

- voice cloning consent dialog/state
- per-session voice cloning warning
- reference clip selection
- reference clip replacement
- Chatterbox/clone-capable model wrapper spike
- cloned TTS take type
- export metadata
- commercial-safe filtering

### Acceptance criteria

- Voice cloning cannot run without consent.
- Voice cloning model must be license-manifested.
- User can choose/replace reference clip.
- Cloned TTS produces a take artifact.
- Export metadata marks voice cloning use.
- Commercial-safe mode enforces allowed models only.

### Non-goals

- No default voice cloning.
- No celebrity/public figure detection.
- No guarantee of voice identity.
- No perfect emotion transfer.

### Risks

- Legal/ethical misuse.
- Model quality variance.
- High memory use.
- Runtime instability.
- User expectations too high.

### Tests

- consent-required test.
- commercial-safe filtering test.
- reference clip persistence test.
- fake cloned TTS artifact test.

### Agent tasks

Good agent prompt:

```text
Implement voice cloning consent state and reference clip selection persistence. Add tests proving clone-capable TTS cannot run without consent. Do not implement real Chatterbox yet.
```

---

## Milestone 14 — Hardware profiler and preset recommendation

### Goal

Benchmark the user’s machine and recommend a practical quality preset.

### Deliverables

Features:

- benchmark scenarios
- benchmark database storage
- RTF calculation
- provider used
- memory measurement where available
- quality preset recommendation
- UI hardware profiler tab

Presets:

```text
Quality
Balanced
Turbo
CPU-safe
```

### Acceptance criteria

- User can run benchmark.
- Results are stored.
- App recommends preset.
- Recommendation can be overridden.
- Runtime plan uses benchmark history where available.

### Non-goals

- No public benchmark leaderboard.
- No telemetry upload.
- No automatic online comparison.

### Risks

- Memory measurement difficulty.
- Benchmarks too slow.
- Synthetic benchmarks not representative.
- Driver updates invalidate assumptions.

### Tests

- preset recommendation tests.
- benchmark persistence tests.
- fake benchmark scenario tests.
- report formatting tests.

### Agent tasks

Good agent prompt:

```text
Implement BenchmarkResult persistence and RecommendPresetHandler using fake benchmark results. Add tests for Quality/Balanced/Turbo/CPU-safe thresholds.
```

---

## Milestone 15 — Model manager and downloads

### Goal

Let users install, verify, and remove model assets safely.

### Deliverables

Features:

- model list UI
- installed/missing state
- download queue
- hash verification
- license display
- commercial-safe badges
- delete model
- repair model cache

### Acceptance criteria

- User can see required models.
- User can download missing models.
- Hash mismatch is detected.
- License is visible before install.
- Commercial-safe status is visible.
- Broken cache can be repaired.

### Non-goals

- No model marketplace.
- No arbitrary user model plugin system yet.
- No auto-update without user awareness.

### Risks

- Hugging Face/network instability.
- Model file size.
- License drift.
- Download interruption.

### Tests

- manifest-driven model list test.
- fake download test.
- hash mismatch test.
- license display test.
- repair cache test.

### Agent tasks

Good agent prompt:

```text
Implement ModelCacheService with fake downloader and hash verifier. Add tests for installed/missing/corrupt states. Do not build marketplace/plugin system.
```

---

## Milestone 16 — Timeline/editor expansion

### Goal

Expand from transcript list to real editorial workspace.

### Deliverables

Features:

- waveform timeline
- speaker lanes
- segment selection
- transport sync
- subtitle overlay
- selected segment inspector
- edit/retranslate/re-TTS commands
- stale indicators

### Acceptance criteria

- Timeline displays waveform and segments.
- Clicking segment selects transcript/translation/TTS state.
- User can preview selected range.
- Stale/missing artifacts visible.
- UI remains responsive during background work.

### Non-goals

- No full nonlinear video editor.
- No arbitrary clip editing.
- No keyframe-heavy DAW features.

### Risks

- UI complexity explodes.
- Timeline consumes too much time before pipeline is robust.
- Sync bugs.

### Tests

- view model tests.
- selection state tests.
- command availability tests.
- manual UI smoke tests.

### Agent tasks

Good agent prompt:

```text
Implement TimelineViewModel state and selection behavior with fake segments. Do not implement custom rendering yet. Add tests for selected segment and stale indicators.
```

---

## Milestone 17 — Commercial-safe mode and licensing UI

### Goal

Make model/provider/license safety visible and enforceable.

### Deliverables

Features:

- commercial-safe mode toggle
- unsafe model blocking
- attribution requirements
- model license panel
- export license manifest
- warnings for voice cloning
- third-party notices generation

### Acceptance criteria

- Unsafe models are disabled in commercial-safe mode.
- User sees why a model is blocked.
- Export includes model/provider manifest.
- Third-party notices can be generated.
- Tests cover unsafe model exclusion.

### Non-goals

- No legal advice engine.
- No guarantee every use case is legally safe.
- No automatic rights clearance.

### Risks

- Users misunderstand “commercial safe.”
- License metadata becomes stale.
- Provider terms vary.

### Tests

- blocking tests.
- export manifest tests.
- attribution tests.
- UI state tests.

### Agent tasks

Good agent prompt:

```text
Implement CommercialSafeModeService and ExportLicenseManifest. It should block unknown and non-commercial models and include attribution-required models in output metadata.
```

---

## Milestone 18 — Packaging and clean-machine install

### Goal

Make Babel Studio install and run on a clean Windows machine without developer tools.

### Deliverables

Features:

- signed installer or MSIX
- bundled required runtime pieces
- FFmpeg packaging decision
- first-run setup wizard
- clean uninstall behavior
- diagnostic bundle
- repair tool

### Acceptance criteria

- Fresh Windows install works without Visual Studio.
- User does not install Python, Conda, Docker, WSL, or CUDA Toolkit.
- First-run setup explains downloads and hardware.
- App can create project and run transcript slice.
- Failure modes are visible.

### Non-goals

- No auto-updater unless packaging path is stable.
- No enterprise deployment tooling yet.

### Risks

- Windows App SDK packaging complexity.
- FFmpeg licensing.
- Antivirus false positives.
- Long model downloads.

### Tests

- clean VM install test.
- no-dev-tools smoke test.
- uninstall/reinstall test.
- first-run failure test.

### Agent tasks

Good agent prompt:

```text
Create packaging checklist and installer smoke script. Do not alter application code. Identify files required for clean-machine run.
```

---

## Milestone 19 — Public alpha

### Goal

Release a limited, honest alpha for technical users.

### Minimum alpha capability

- open local media
- create/reopen project
- extract audio
- run transcript stage
- edit transcript
- run translation stage
- run stock TTS stage
- preview dubbed segment or range
- export at least audio or caption output
- diagnostics bundle
- model/license visibility

### Alpha messaging

Say:

```text
Babel Studio is an early local-first dubbing workstation.
Expect rough edges.
Do not rely on it for production/commercial work without validating model licenses and output quality.
```

### Non-goals

- No broad consumer marketing.
- No paid plans yet.
- No voice cloning as headline feature.

### Risks

- Users expect finished product.
- Model downloads are too large.
- Runtime failures are underdiagnosed.
- Support burden spikes.

### Tests

- public release smoke script.
- clean install.
- project reopen.
- diagnostics export.
- common failure paths.

---

## Milestone 20 — Beta and monetization readiness

### Goal

Prepare for serious user adoption and optional monetization.

### Required before monetization

- stable installer
- stable project persistence
- export works
- diagnostics works
- commercial-safe mode works
- model licenses audited
- contribution licensing handled
- support channel established
- privacy policy
- terms/commercial license draft

### Monetization options

Recommended:

```text
Free GPL community edition
GitHub Sponsors / Ko-fi donations
Commercial license for non-GPL organizational use
Optional paid cloud credits later
Support contracts for businesses
```

Do not monetize:

- save/reopen
- export
- diagnostics
- license visibility
- privacy controls
- basic local transcript path

### Acceptance criteria

- Clear public license posture.
- Commercial license draft exists.
- CLA process exists for contributors.
- Model license manifest is complete for included models.
- Public docs explain limitations.

---

## Milestone dependency map

```text
M0 Repo foundation
  ↓
M1 Runtime harness
  ↓
M2 Model manifest/license policy
  ↓
M3 SQLite project spine
  ↓
M4 Media ingest/artifact store
  ↓
M5 Runtime planner/model cache
  ↓
M6 Transcript vertical slice
  ↓
M7 Translation slice
  ↓
M8 Stock TTS slice
  ↓
M9 Preview mix
  ↓
M10 Export
  ↓
M11 Diarization
  ↓
M12 Stem separation
  ↓
M13 Voice cloning
  ↓
M14 Hardware profiler
  ↓
M15 Model manager
  ↓
M16 Timeline/editor expansion
  ↓
M17 Commercial-safe licensing UI
  ↓
M18 Packaging
  ↓
M19 Public alpha
  ↓
M20 Beta/monetization readiness
```

Some milestones can overlap after the transcript slice is stable:

```text
M14 Hardware profiler can begin after M5.
M15 Model manager can begin after M2 and M5.
M17 Licensing UI can begin after M2.
M18 Packaging can begin after M6.
```

But do not let overlap become chaos. The first real gate is still the transcript vertical slice.

---

## Final rule

Do not chase the impressive feature first.

The correct build order is:

```text
runtime proof
state proof
media proof
transcript proof
translation proof
TTS proof
preview proof
export proof
then voice cloning
```

Babel Studio becomes valuable when it is reliable enough that users can trust the pipeline. The architecture and milestones should protect that trust from day one.
