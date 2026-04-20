# Phase 0 Completion Note

- Phase: 0
- Title: Repository and architecture foundation
- Status: Complete
- Date: 2026-04-19

## Summary

Phase 0 is complete. The repository now has the foundation documents, boundary rules, directory structure, and architectural decision records needed to keep early work scoped and to prevent the project from collapsing into unverified full-app implementation.

The main outcome of this phase is not runnable product code. The outcome is a repo that clearly states what Babel Studio is, what it is not, and where future code is allowed to live.

## Completed in this phase

- Repository purpose and build philosophy are documented in `README.md`.
- Agent guardrails are documented in `AGENTS.md`.
- Licensing and commercial-safety groundwork exists in:
  - `LICENSE`
  - `COMMERCIAL-LICENSE.md`
  - `CONTRIBUTOR-LICENSE-AGREEMENT.md`
  - `MODEL_LICENSE_POLICY.md`
  - `THIRD_PARTY_NOTICES.md`
- Architecture guidance now lives in `docs/architecture/ARCHITECTURE.md`.
- Milestone planning now lives in `docs/milestones/MILESTONE.md`.
- ADRs were drafted for the two key Phase 0 decisions:
  - `docs/adr/ADR-0001-winui3-windows-ml.md`
  - `docs/adr/ADR-0002-sqlite-project-persistence.md`
- Directory-level `README.md` files exist across the important source, docs, tests, packaging, samples, and assets subtrees.
- `ARCHITECTURE.md` was reviewed for internal contradictions and aligned with the current document set.

## Acceptance criteria assessment

### Repo clearly states this is early architecture/prototype work

Met. The root `README.md`, milestone plan, and agent rules all emphasize proving the runtime and persistence spine before building the full editor.

### GPLv3 plus commercial dual-license intent is documented

Met. The repository includes the GPL license, commercial-license note, CLA, and model license policy.

### Agent guardrails exist

Met. `AGENTS.md` sets explicit boundaries around inference placement, persistence placement, runtime honesty, commercial-safe mode, and scope control.

### Model license policy exists before any model is added

Met. `MODEL_LICENSE_POLICY.md` is present and ADR/pipeline guidance reinforces manifest-first model onboarding.

### Directory boundaries are clear enough for scoped agent work

Met. The repo has per-directory `README.md` files plus architecture and ADR documentation describing project ownership and dependency direction.

## Deviations from the original milestone text

- The architecture and milestone documents are currently organized under `docs/architecture` and `docs/milestones` rather than living at the repository root.
- That relocation is acceptable for Phase 0 because the intent of the milestone was clarity and boundary-setting, and the documentation is now more consistently grouped under `docs/`.

## Risks closed by Phase 0

- Unclear project boundaries between UI, application, inference, media, and persistence.
- Missing licensing and commercial-safety groundwork before adding models.
- Agent-driven scope creep into speculative full-app implementation.
- Architectural ambiguity around WinUI 3, Windows ML / ONNX Runtime, and SQLite-backed project persistence.

## Remaining risks intentionally deferred

- Runtime viability of real ONNX models on target Windows hardware.
- Benchmark and provider-selection correctness.
- Media probe and normalization correctness.
- Real persistence and migration behavior in SQLite.
- Any end-user UI or workflow validation.

## Exit recommendation

Phase 0 can be treated as closed. The next work should begin at Milestone 1 and stay narrow:

1. Build the runtime viability harness.
2. Prove one ONNX model can load and run from C#.
3. Record benchmark output without introducing UI.

Do not expand into the editor shell, full pipeline orchestration, or plugin ideas until that harness path is boring and repeatable.
