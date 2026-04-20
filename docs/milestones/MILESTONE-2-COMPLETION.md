# Milestone 2 Completion Note

- Milestone: 2
- Title: Model manifest and commercial-safe policy
- Status: Complete
- Date: 2026-04-19

## Summary

Milestone 2 is complete. Babel Studio now has a first-class model manifest layer, explicit commercial-safe evaluation rules, hash-verification policy handling, and a minimal infrastructure boundary for local model-cache records.

The main outcome of this milestone is not model acquisition or UI management. The outcome is a runtime-owned policy and metadata spine that prevents model handling from scattering across benchmark code, wrappers, or future app layers.

## Completed in this milestone

- `src/BabelStudio.Inference/Runtime/ModelManifest/` now contains:
  - `ModelManifest` and related manifest/value types
  - manifest loading and validation
  - commercial-safe evaluation
  - hash-verification policy handling
  - bundled manifest registry integration
  - a JSON schema for manifest documents
- `src/BabelStudio.Domain/` now contains the local cache record contract used to describe machine-local model cache state.
- `src/BabelStudio.Infrastructure/` now contains the minimal supporting infrastructure needed for this milestone:
  - local storage path resolution
  - SHA-256 file hashing
  - JSON-backed local cache record persistence
- `tests/BabelStudio.Inference.Tests/` covers manifest parsing, validation failures, commercial-safe rules, hash verification behavior, and local cache record persistence round-tripping.
- `src/BabelStudio.Inference.Onnx/` continues to consume the bundled manifest registry so benchmark aliasing and variant resolution sit on top of the same manifest foundation.

## Acceptance criteria assessment

### Unknown-license models are not commercial-safe

Met. `CommercialSafeEvaluator` rejects unknown-license models, and tests verify that behavior.

### Non-commercial models are not commercial-safe

Met. `CommercialSafeEvaluator` rejects non-commercial models, and tests verify that behavior.

### Voice-cloning models require consent

Met. Loader validation rejects voice-cloning manifests that omit consent, and evaluator output preserves consent requirements.

### Manifests can be loaded and validated

Met. `ModelManifestLoader` loads both single-manifest and catalog-form documents and enforces required fields plus value validation.

### Invalid manifests produce useful errors

Met. Validation failures include field-specific messages for invalid license values, invalid task values, invalid hash-verification mode, duplicate aliases, missing required fields, and contradictory policy flags.

### Tests cover commercial-safe logic

Met. `BabelStudio.Inference.Tests` explicitly covers unknown-license rejection, non-commercial rejection, attribution flags, and consent requirements.

## Deviations from the original milestone text

- The milestone originally listed `src/BabelStudio.Infrastructure/` as a deliverable before any concrete infrastructure code existed. The repo now includes the minimum honest implementation needed for this milestone rather than leaving that project as placeholders only.
- Hash verification is implemented as policy evaluation plus local file verification, not as part of a downloader workflow. That matches the milestone’s non-goals and avoids sneaking model acquisition into scope.
- The bundled model manifest registry was initially introduced to support benchmark aliasing in Milestone 1; in this milestone it was normalized onto the first-class manifest loader so benchmark resolution no longer sits on an ad hoc metadata path.

## Risks closed by Milestone 2

- Model metadata scattering across benchmark code, wrappers, or future UI code.
- Silent invention of commercial-safe status for unknown or non-commercial models.
- Weak validation around malformed manifest data.
- Missing machine-local boundary for cache metadata and file-hash verification.

## Remaining risks intentionally deferred

- Real model download workflows and remote-source verification.
- UI model manager and user-facing consent flows.
- Final legal review of model cards, exported ONNX artifacts, and redistribution obligations.
- Automatic external registry or Hugging Face metadata lookup.
- Runtime route planning beyond benchmark/provider selection.

## Exit recommendation

Milestone 2 can be treated as closed. The next work should move into the next vertical slice and stay narrow:

1. Keep manifests as the only allowed path for adding or resolving models.
2. Use the existing benchmark harness as the proving ground for any new model family.
3. Add downloader, cache-population, or runtime-selection behavior only as separate scoped work, not as hidden side effects of manifest parsing.

Do not let future inference wrappers, UI code, or persistence code reintroduce license-policy decisions outside the manifest and evaluator layer.
