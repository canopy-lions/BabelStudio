# Milestone 6 Completion Note

- Milestone: 6
- Title: Transcript-only vertical slice
- Status: Complete
- Date: 2026-04-20
- Revision note: Updated to reflect the current repo state after the real ONNX stage-engine swap landed behind the same Milestone 6 boundaries.

## Summary

Milestone 6 is complete. Babel Studio now has its first real product slice: a minimal WinUI shell can open local media, create a `.babelstudio` project, run the existing media-ingest pipeline, generate a transcript through the stage boundary, display transcript segments, persist manual edits as a new transcript revision, and reopen the project without recomputing.

The current repo state now carries that transcript slice on real ONNX-backed VAD/ASR inside the product shell. The app composes `SileroVadSpeechRegionDetector` and `WhisperOnnxAudioTranscriptionEngine` behind the existing contracts, uses runtime planning/provider selection to choose the execution route, and preserves transcript revision plus stage-run/artifact provenance rather than regressing to in-place mutation.

## Completed in this milestone

- `src/BabelStudio.App/` now contains a minimal WinUI 3 unpackaged shell with:
  - media-file open flow
  - existing-project open flow
  - transcript list display
  - manual transcript editing
  - save-to-new-revision behavior
  - stage/source status summary, including selected runtime provider when available
- `src/BabelStudio.Contracts/` is now a buildable project and defines the stage-facing speech-region and transcription contracts shared by application and inference layers.
- `src/BabelStudio.Application/` now contains:
  - `TranscriptProjectService`
  - transcript repository/stage-run store abstractions
  - create/open/edit transcript workflows layered on top of `ProjectMediaIngestService`
  - stage-run runtime-summary capture so requested provider, selected provider, model identity, and bootstrap detail are preserved with the transcript pipeline
- `src/BabelStudio.Inference.Onnx/` now contains the concrete transcript-stage engines:
  - `SileroVadSpeechRegionDetector`
  - `WhisperOnnxAudioTranscriptionEngine`
  - ONNX session/bootstrap wiring plus runtime-planner/provider-selection integration for the transcript slice
- `src/BabelStudio.Infrastructure/` now persists:
  - transcript revisions
  - transcript segments
  - stage runs for `vad` and `asr`
  - transcript/speech-region artifacts with provenance and optional `StageRunId`
  - stage-run runtime context such as requested provider, selected provider, model identity, and bootstrap detail
- `src/BabelStudio.Media/` remains the real ingest/extraction/probe path used by the new app flow.
- `BabelStudio.sln` now includes the app, contracts, media, and transcript-related test projects so the solution build reflects the transcript slice.

## Acceptance criteria assessment

### User can open a media file

Met. The WinUI shell exposes `Open Media` and creates a project root from the selected source file.

### App creates project

Met. The app uses `ProjectMediaIngestService` to create the project layout, manifest, SQLite database, source reference, normalized audio, and waveform artifacts.

### App extracts audio

Met. Project creation still runs the real FFmpeg-backed normalized-audio extraction path from Milestone 4.

### App runs VAD/ASR through validated path or test/fake engine

Met. The transcript slice runs through explicit `ISpeechRegionDetector` and `IAudioTranscriptionEngine` boundaries using real ONNX-backed `SileroVadSpeechRegionDetector` and `WhisperOnnxAudioTranscriptionEngine` implementations selected through the runtime planner.

### Transcript segments appear

Met. Generated transcript segments are stored in the project database and shown in the WinUI transcript list.

### User can edit transcript text

Met. Transcript rows are editable in the shell and the app can persist the edits.

### Edits persist

Met. Saving transcript edits creates a new transcript revision instead of overwriting the generated revision.

### Project reopens without recomputing

Met. Reopening loads the latest stored transcript revision and segment list from the project database without rerunning ingest or transcription.

### Stage run and artifact provenance are stored

Met. The project database now stores `vad` / `asr` stage runs, transcript revisions linked to stage runs, transcript/speech-region artifacts with provenance metadata, and runtime-route details such as requested provider, selected provider, model identity, and bootstrap detail.

## Deviations from the original milestone text and original closeout note

- The milestone acceptance text allowed a validated path or a test/fake engine. The current repo state now goes beyond that minimum by wiring the shipped transcript slice to real ONNX-backed VAD/ASR while preserving the same contract boundary.
- Transcript edits do not overwrite generated transcript rows in place. Instead, the app creates a new transcript revision so generated-stage provenance is preserved.
- The repo already contained two persistence tracks. Milestone 6 extends the project-local ingest database so the transcript slice can use the real ingest artifacts immediately instead of pausing for a larger persistence unification.

## Risks closed by Milestone 6

- No operator-visible product slice over the ingest/runtime groundwork.
- No persisted transcript revision model.
- No app-level reopen path that could prove transcript persistence without recomputation.
- No stored provenance linking transcript outputs back to stage execution.

## Remaining risks intentionally deferred

- Broader runtime hardening across more machines, providers, and bundled model combinations beyond the current transcript-stage baseline.
- Speaker diarization, translation, TTS, and export workflows.
- Rich editing UX beyond the basic transcript list.
- Packaging, distribution, and deployment concerns for the WinUI app.

## Exit recommendation

Milestone 6 can still be treated as closed. With the real stage-engine swap now landed behind the same contracts, the next work should move into transcript-dependent follow-on work and stay narrow:

1. Begin Milestone 7 translation work on top of transcript revisions, not on direct media reprocessing from the UI.
2. Preserve the current revision/provenance/runtime-route model as later stages arrive instead of regressing to in-place mutation.
3. Keep speaker diarization, TTS, and export work out of scope until the translation slice is stable.
