# Milestone 3 Completion Note

- Milestone: 3
- Title: SQLite project spine
- Status: Complete
- Date: 2026-04-19

## Summary

Milestone 3 is complete. Babel Studio now has a durable SQLite-backed project spine with ordered migrations, core project and artifact persistence, stage-run tracking, model-cache persistence, and benchmark-run persistence.

The main outcome of this milestone is not pipeline execution. The outcome is a stable local project database and repository boundary that later media, transcript, and inference stages can build on without inventing their own storage paths or state models.

## Completed in this milestone

- `src/BabelStudio.Application/` now includes persistence-facing interfaces for:
  - connection factory
  - database migrator
  - project repository
  - artifact repository
  - stage run repository
  - model cache repository
  - benchmark repository
- `src/BabelStudio.Domain/` now contains the core persistence records and invariants for:
  - schema versions
  - projects
  - artifacts
  - stage runs
  - benchmark runs
  - local model cache state
- `src/BabelStudio.Infrastructure/` now includes:
  - SQLite connection factory
  - ordered migration runner
  - SQLite schema migrations for the project spine tables
  - Dapper-backed repository implementations
- The schema now creates the milestone tables, including:
  - `SchemaVersion`
  - `Projects`
  - `MediaAssets`
  - `StageRuns`
  - `Artifacts`
  - `Speakers`
  - `SpeakerTurns`
  - `TranscriptRevisions`
  - `TranscriptSegments`
  - `Words`
  - `TranslationRevisions`
  - `TranslatedSegments`
  - `VoiceAssignments`
  - `TtsTakes`
  - `MixPlans`
  - `Exports`
  - `ModelCache`
  - `BenchmarkRuns`
  - `ConsentRecords`
- `tests/BabelStudio.Domain.Tests/` covers the current domain invariants for project creation, artifact path rules, and stage-run completion rules.
- `tests/BabelStudio.Infrastructure.Tests/` covers migration ordering, database creation from scratch, project reopen behavior, artifact registration, stage-run completion, model-cache upsert behavior, and transaction rollback using temporary SQLite files.

## Acceptance criteria assessment

### Database can be created from scratch

Met. The SQLite migrator creates a new database file and applies the schema from an empty starting state.

### Migrations apply in order

Met. `SchemaVersion` records ordered application of the migration set, and tests verify that the applied versions are `1`, `2`, then `3`.

### Project can be created

Met. The project repository can persist a new project record and read it back.

### Stage run can be created and completed

Met. The stage-run repository supports initial creation and later completion with persisted status and completion timestamp.

### Artifact can be registered with hash/path/kind/provenance

Met. The artifact repository stores project-relative path, content hash, artifact kind, provenance, and optional stage-run linkage.

### Project can be reopened

Met. Tests reopen the SQLite database and successfully reload persisted project state.

### Tests use temporary SQLite files

Met. The infrastructure tests create and dispose temporary on-disk SQLite files for each test case.

## Deviations from the original milestone text

- The milestone listed `tests/BabelStudio.Domain.Tests/` and `tests/BabelStudio.Infrastructure.Tests/` as deliverables even though those projects did not yet exist. They are now real test projects rather than placeholders.
- The full table list is created now, but only the milestone-required repositories were implemented as first-class repository types. The remaining tables exist as schema groundwork and are not yet wrapped in richer application services.
- The repository layer includes benchmark persistence because benchmark state already existed elsewhere in the repo and belongs in the same durable project/database spine.

## Risks closed by Milestone 3

- Ad hoc local state spread across files without a durable project database.
- Missing migration ordering and schema-version tracking.
- Weak or inconsistent artifact provenance storage.
- No transaction boundary for multi-step persistence work.

## Remaining risks intentionally deferred

- Media ingest and artifact-folder population.
- Source media fingerprinting and waveform generation.
- Transcript, translation, TTS, and export workflows beyond schema groundwork.
- UI project opening, browsing, and diagnostics.
- Recovery strategies for destructive or partially failed future migrations.

## Exit recommendation

Milestone 3 can be treated as closed. The next work should move into Milestone 4 and stay narrow:

1. Open a local media file through a media-probe abstraction.
2. Extract normalized working audio through an FFmpeg-backed service.
3. Register the resulting media artifacts through the existing SQLite project spine.

Do not skip directly into ASR, translation, or UI work before the media ingest and artifact-store path is boring and repeatable.
