# Milestone 6 Completion Note

- Milestone: 6
- Title: Transcript-only vertical slice
- Status: Complete
- Date: 2026-04-20

## Summary

Milestone 6 is complete. Babel Studio now has its first real product slice: a minimal WinUI shell can open local media, create a `.babelstudio` project, run the existing media-ingest pipeline, generate a transcript through the stage boundary, display transcript segments, persist manual edits as a new transcript revision, and reopen the project without recomputing.

This milestone intentionally stops short of real ONNX-backed VAD/ASR inside the product shell. The transcript slice runs through a deterministic inference boundary today so the application, persistence, and artifact/provenance path are proven before the Windows ML execution path replaces the scripted engine in a later milestone.

## Completed in this milestone

- `src/BabelStudio.App/` now contains a minimal WinUI 3 unpackaged shell with:
  - media-file open flow
  - existing-project open flow
  - transcript list display
  - manual transcript editing
  - save-to-new-revision behavior
  - stage/source status summary
- `src/BabelStudio.Contracts/` is now a buildable project and defines the stage-facing speech-region and transcription contracts shared by application and inference layers.
- `src/BabelStudio.Application/` now contains:
  - `TranscriptProjectService`
  - transcript repository/stage-run store abstractions
  - create/open/edit transcript workflows layered on top of `ProjectMediaIngestService`
- `src/BabelStudio.Inference/` now contains scripted `ISpeechRegionDetector` and `IAudioTranscriptionEngine` implementations used for the transcript slice.
- `src/BabelStudio.Infrastructure/` now persists:
  - transcript revisions
  - transcript segments
  - stage runs for `vad` and `asr`
  - transcript/speech-region artifacts with provenance and optional `StageRunId`
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

Met. The transcript slice runs through explicit `ISpeechRegionDetector` and `IAudioTranscriptionEngine` boundaries using scripted implementations in this milestone.

### Transcript segments appear

Met. Generated transcript segments are stored in the project database and shown in the WinUI transcript list.

### User can edit transcript text

Met. Transcript rows are editable in the shell and the app can persist the edits.

### Edits persist

Met. Saving transcript edits creates a new transcript revision instead of overwriting the generated revision.

### Project reopens without recomputing

Met. Reopening loads the latest stored transcript revision and segment list from the project database without rerunning ingest or transcription.

### Stage run and artifact provenance are stored

Met. The project database now stores `vad` / `asr` stage runs, transcript revisions linked to stage runs, and transcript/speech-region artifacts with provenance metadata.

## Deviations from the original milestone text

- The milestone called for a WinUI transcript slice and that is now present, but the shipped stage implementation is intentionally scripted instead of ONNX-backed. This is allowed by the milestone acceptance text and keeps the UI slice stable while the validated Windows ML path remains isolated.
- Transcript edits do not overwrite generated transcript rows in place. Instead, the app creates a new transcript revision so generated-stage provenance is preserved.
- The repo already contained two persistence tracks. Milestone 6 extends the project-local ingest database so the transcript slice can use the real ingest artifacts immediately instead of pausing for a larger persistence unification.

## Risks closed by Milestone 6

- No operator-visible product slice over the ingest/runtime groundwork.
- No persisted transcript revision model.
- No app-level reopen path that could prove transcript persistence without recomputation.
- No stored provenance linking transcript outputs back to stage execution.

## Remaining risks intentionally deferred

- Replacing the scripted VAD/ASR implementations with the Windows ML / ONNX path.
- Speaker diarization, translation, TTS, and export workflows.
- Rich editing UX beyond the basic transcript list.
- Packaging, distribution, and deployment concerns for the WinUI app.

## Exit recommendation

Milestone 6 can be treated as closed. The next work should move into the first real stage-engine swap and stay narrow:

1. Replace the scripted VAD/ASR implementations with the validated ONNX execution path behind the same contracts.
2. Preserve the current revision/provenance model as real ASR arrives instead of regressing to in-place mutation.
3. Keep translation and TTS work dependent on transcript revisions, not on direct media reprocessing from the UI.
