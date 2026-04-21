# Milestone 7 Completion Note

- Milestone: 7
- Title: Translation slice
- Status: Complete
- Date: 2026-04-21
- Revision note: Written against the revised root `MILESTONE.md` and the current repo state after the direct Opus-MT `en <-> es` slice and tokenizer fix were verified in the live app.

## Summary

Milestone 7 is complete. Babel Studio now extends the transcript vertical slice with a real translation workflow inside the existing WinUI shell. A user can declare the transcript language as English or Spanish, generate a direct opposite-language draft translation through real ONNX-backed Opus-MT, edit translated text, save a new translation revision, reopen the project without recomputing, and see `Needs Refresh` when the transcript changes after translation.

The current repo state goes beyond the narrow fake-first interpretation of the original milestone. The shipped slice is already wired to real direct-pair translation for `English -> Spanish` and `Spanish -> English`, while still keeping fake translation test doubles in the application test layer.

## Completed in this milestone

- `src/BabelStudio.App/` now contains the translation-facing shell additions:
  - transcript-language selection (`English` or `Spanish`)
  - `Translate` / `Re-translate` action for the direct opposite-language pair
  - translated text editors shown alongside transcript segments
  - translation revision summary and `Needs Refresh` state in the status area
  - `Save Translation` flow that creates a new revision instead of mutating generated output in place
- `src/BabelStudio.Application/` keeps `TranscriptProjectService` as the single workspace service and now adds:
  - translation generation from the current transcript revision
  - manual translation-save flow
  - transcript-language persistence in the project manifest
  - derived refresh-needed state when a translation no longer matches the latest transcript revision
  - translation artifact writing and provenance registration
- `src/BabelStudio.Domain/` now contains:
  - `TranslationRevision`
  - `TranslatedSegment`
  - `ArtifactKind.TranslationRevision`
- `src/BabelStudio.Infrastructure/` now persists:
  - translation revisions
  - translated segments
  - per-target-language revision numbering
  - translation-stage runtime/provider/model provenance through the existing stage-run path
- `src/BabelStudio.Inference/` and `src/BabelStudio.Inference.Onnx/` now contain:
  - translation-aware runtime planning inputs with source and target language codes
  - direct-pair planner support for `en <-> es`
  - `OpusMtTranslationEngine`
  - runtime-summary capture for the translation stage so requested provider, selected provider, model identity, and bootstrap detail are preserved
- `tests/` now covers:
  - application-layer translation revision persistence with `FakeTranslationEngine`
  - SQLite translation repository round-tripping
  - planner behavior for supported and unsupported translation pairs
  - ONNX-backed direct translation in both directions
  - tokenizer/model-ID regression coverage for the Opus-MT wrapper

## Acceptance criteria assessment

### User can translate English transcript segments to Spanish

Met. The app now supports direct `en -> es` translation through the real Opus-MT ONNX path.

### User can translate Spanish transcript segments to English

Met. The app now supports direct `es -> en` translation through the real Opus-MT ONNX path.

### Translation results are stored as a revision

Met. Generated translations are stored as revisioned project data rather than replacing transcript data in place.

### User can edit translated text

Met. The WinUI shell exposes editable translated text and a save path for manual translation revisions.

### Transcript edit marks affected translation `Needs Refresh`

Met. Refresh-needed state is derived from revision linkage and surfaced in the UI as `Needs Refresh`.

### Reopen preserves translation

Met. Reopening the `.babelstudio` project loads the latest stored translation revision without forcing translation to rerun.

### Model/provider used is recorded

Met. Translation stage runs preserve runtime metadata such as requested provider, selected provider, model identity, and bootstrap detail.

## Deviations from the original milestone text and revised plan framing

- The current repo state already uses real ONNX-backed direct-pair translation in Milestone 7. The root revised plan now treats Milestone 9 as the expansion milestone for broader routing and MADLAD pivot support, not as the first point where translation becomes real.
- The original milestone test wording still referenced fake translation as the narrow validation route. The repo now goes beyond that minimum by shipping the real direct-pair Opus-MT path while still keeping fakes for fast application tests.
- Translation remains intentionally bounded to the direct `English <-> Spanish` pairs in Milestone 7. Free-form target selection, broader pair routing, and pivot fallback stay deferred to later work.

## Risks closed by Milestone 7

- No persisted translation revision model layered on top of transcript revisions.
- No user-visible translation workflow in the WinUI shell.
- No refresh-needed behavior when transcript edits invalidate an earlier translation.
- No translation-stage provenance linking saved translated output back to the runtime route used.
- No regression coverage for the Opus-MT tokenizer/model-ID mapping used by the real translation path.

## Verification

### Automated verification completed

Completed on 2026-04-21 local time during Milestone 7 implementation and translation-fix validation.

Executed:

- `dotnet build BabelStudio.sln -v minimal -p:UseSharedCompilation=false`
- `dotnet test tests\BabelStudio.Inference.Tests\BabelStudio.Inference.Tests.csproj -v minimal -p:UseSharedCompilation=false`
- `dotnet test BabelStudio.sln -v minimal --no-build`

### Manual smoke test completed

Completed on 2026-04-21 local time during live app validation.

Verified workflow:

- launched the WinUI app
- opened local media into a `.babelstudio` project
- confirmed transcript generation completed
- selected transcript language in the app
- ran direct translation in the live shell
- verified translated text could be edited and saved as a new revision
- closed and reopened the app
- used `Open Project` to reopen the saved `.babelstudio` folder
- confirmed the translation revision persisted without recomputing on reopen
- reran the Spanish-to-English translation after the tokenizer fix and confirmed the output was now sensible instead of corrupted token noise

## Remaining risks intentionally deferred

- Broader multi-language routing beyond the direct `English <-> Spanish` pairs.
- MADLAD pivot translation for unsupported pairs.
- Glossary/terminology control, subtitle overlay polish, and playback-synced translation review beyond the current shell.
- Translation quality limits inherent to the current bundled Opus-MT models.
- TTS, diarization, export, and packaging work outside the translation slice.

## Exit recommendation

Milestone 7 can be treated as closed.

The next translation-related milestone should not redo the direct `en <-> es` slice. It should build on the current real translation baseline and move into the expanded routing and playback-integrated review work described for Milestone 9, while Milestone 8 proceeds with player, segment-editor, and project-management fundamentals.
