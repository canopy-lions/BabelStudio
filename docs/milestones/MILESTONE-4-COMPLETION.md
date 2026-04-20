# Milestone 4 Completion Note

- Milestone: 4
- Title: Media ingest and artifact store
- Status: Complete
- Date: 2026-04-20

## Summary

Milestone 4 is complete. Babel Studio now has a first-class media ingest path that can open a local media file, probe it through FFmpeg tooling, extract normalized working audio, fingerprint the source and generated files, write project artifacts atomically, and reopen the project with the registered media artifacts intact.

The main outcome of this milestone is not transcript generation or UI workflow. The outcome is a boring, repeatable ingest boundary that converts arbitrary local media into stable project artifacts and durable repository records for later stages to consume.

## Completed in this milestone

- `src/BabelStudio.Application/` now includes the media-ingest contracts and orchestration for:
  - media probing
  - normalized audio extraction
  - file fingerprinting
  - waveform summary generation
  - artifact-store writes
  - media-asset persistence
  - project creation and reopen flows through `ProjectMediaIngestService`
- `src/BabelStudio.Media/` now includes:
  - `FfmpegMediaProbe`
  - `FfmpegAudioExtractionService`
  - `FfmpegToolResolver`
  - `ProcessRunner`
  - PCM WAV inspection helpers
  - waveform-summary generation
- `src/BabelStudio.Infrastructure/` now includes:
  - `FileSystemArtifactStore` with atomic write-handle and commit behavior
  - SHA-256 file fingerprinting
  - SQLite-backed media-asset and artifact persistence through `SqliteMediaAssetRepository`
- The project artifact layout is now populated and used for ingest outputs, including:
  - `manifest.json`
  - `media/source-reference.json`
  - `media/normalized_audio.wav`
  - `artifacts/waveform/...`
- `tests/BabelStudio.Media.Tests/` covers FFmpeg probing and normalized audio extraction using small fixture media.
- `tests/BabelStudio.Infrastructure.Tests/` covers atomic artifact writes and media-asset repository reopen behavior.
- `tests/BabelStudio.Application.Tests/` covers end-to-end ingest registration and missing-source reopen reporting.

## Acceptance criteria assessment

### Local video/audio can be probed

Met. `FfmpegMediaProbe` uses `ffprobe` process execution and the media test suite verifies probing against fixture media.

### Source media metadata is stored

Met. Ingest writes `media/source-reference.json`, fingerprints the source file, and persists the primary `MediaAsset` record.

### Working audio is extracted

Met. `FfmpegAudioExtractionService` produces normalized mono PCM WAV output and the media tests verify the resulting sample rate and channel count.

### Audio artifact is registered with hash and duration

Met. The ingest flow fingerprints the normalized WAV file and persists it as a `NormalizedAudio` artifact with duration and audio-format metadata.

### Waveform summary is generated and registered

Met. The waveform generator reads the normalized WAV, produces summary buckets, writes the waveform artifact, and registers it in the media-asset repository.

### Project can be reopened and artifacts found

Met. Application and infrastructure tests reopen persisted project state and reload the stored media asset plus registered artifacts.

### Missing source file is reported clearly

Met. The application reopen flow returns `SourceMediaStatus.Missing` with an explicit missing-file message, and tests cover that behavior.

## Deviations from the original milestone text

- The milestone listed `tests/BabelStudio.Media.Tests/` as the primary test deliverable, but the finished slice also required `tests/BabelStudio.Application.Tests/` and `tests/BabelStudio.Infrastructure.Tests/` coverage to verify orchestration, repository reopen behavior, and atomic writes.
- The artifact-folder layout includes the milestone-required paths while still relying on the existing project/database spine introduced in Milestone 3 rather than inventing a second persistence path.
- FFmpeg availability is handled through tool resolution and process execution boundaries, but this milestone intentionally stops at local tooling integration and does not attempt packaging or installer-time dependency management.

## Risks closed by Milestone 4

- No reliable boundary between arbitrary local media and project-managed artifacts.
- Ad hoc or non-atomic artifact writes during ingest.
- Missing provenance for source media and generated audio artifacts.
- Inability to reopen a project and rediscover its normalized working media.

## Remaining risks intentionally deferred

- Clean-machine FFmpeg packaging and distribution.
- Broader container/codec edge cases beyond the current fixture and probe/extract coverage.
- Transcript, translation, TTS, and export stages that consume the ingest artifacts.
- UI browsing, preview, and operator diagnostics for ingest failures.

## Exit recommendation

Milestone 4 can be treated as closed. The next work should move into Milestone 5 and stay narrow:

1. Introduce a runtime planner that selects local execution routes explicitly.
2. Expand model-cache state around the media/transcript path without bypassing the project spine.
3. Keep transcript work dependent on the normalized-audio artifacts created here rather than inventing alternate ingest paths.

Do not skip the runtime-planning and model-cache layer by letting ASR or translation features reach directly into FFmpeg/process code from the UI or model wrappers.
