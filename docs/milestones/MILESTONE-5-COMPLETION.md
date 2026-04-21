# Milestone 5 Completion Note

- Milestone: 5
- Title: Runtime planner and machine-local cache visibility
- Status: Complete
- Date: 2026-04-20

## Summary

Milestone 5 is complete. Babel Studio now has a planner-only runtime selection layer that can evaluate the current machine, inspect the bundled manifest inventory, inspect the machine-local model cache index, and return a stable runtime plan for the next stage without pulling model selection logic into the app layer or model wrappers.

This milestone does not execute ONNX models, start stage runs, or add any UI workflow. The outcome is a durable planning boundary that decides whether a stage is ready, requires a download, or is blocked, with machine-readable fallback and warning metadata that later milestones can persist into diagnostics and run records.

## Completed in this milestone

- `src/BabelStudio.Domain/Common/RuntimePlanning.cs` now defines shared runtime-planning enums and diagnostic codes for:
  - `RuntimeStage`
  - `ExecutionProviderKind`
  - `StageRuntimePlanStatus`
  - `RuntimePlanFallbackCode`
  - `RuntimePlanWarningCode`
- `src/BabelStudio.Inference/Runtime/Planning/` now includes:
  - `IRuntimePlanner`
  - planner request/plan records
  - milestone-scoped stage requirements
  - provider discovery and smoke-test abstractions
  - `RuntimePlanner`
- The planner reuses the existing bundled manifest registry and commercial-safe evaluation path rather than introducing a parallel metadata catalog.
- Milestone 5 stage coverage is intentionally limited to:
  - `Vad`
  - `Asr`
- The planner now applies the milestone-scoped provider rule:
  - supported providers in Milestone 5 are `DirectMl` and `Cpu`
  - non-CPU providers never return `Ready` without a passing smoke test for the selected model/provider/variant
- The planner resolves installed-model visibility through the machine-local cache index, not project-local SQLite, for Milestone 5 planning.
- `src/BabelStudio.Infrastructure/Runtime/Planning/LocalModelCacheInventory.cs` now provides the only concrete infrastructure adapter added for this milestone.
- `tests/BabelStudio.Inference.Tests/RuntimePlannerTests.cs` now covers:
  - DirectML-ready planning
  - CPU fallback after provider discovery/smoke-test failure
  - download-required planning when the cache is missing
  - commercial-safe filtering
  - explicit blocked ASR in commercial-safe mode for the current bundled inventory
  - VAD remaining plannable in commercial-safe mode
  - stable JSON round-tripping for future persisted diagnostics

## Acceptance criteria assessment

### Planner can choose a local runtime route

Met. `RuntimePlanner` selects a model, provider, and variant for Milestone 5-supported stages based on manifest metadata, machine-local cache visibility, and provider availability.

### Non-CPU readiness is gated by smoke testing

Met. The planner does not return `Ready` for `DirectMl` unless the selected model/provider/variant passes the required smoke test.

### Machine-local cache visibility is available to planning

Met. The planner reads installed-model visibility through the machine-local cache record store via `LocalModelCacheInventory`.

### Planner output is stable and persistable

Met. `StageRuntimePlan` is a stable diagnostics/logging shape with enum-based fallback and warning codes and no absolute machine-local paths in the persisted plan object.

### Commercial-safe filtering is enforced

Met. The planner filters candidates through `CommercialSafeEvaluator` before provider and variant selection.

### Commercial-safe ASR blocking is explicit

Met. With the current bundled manifest inventory, `Asr` in commercial-safe mode returns `Blocked`, and that behavior is covered explicitly in tests and planner documentation.

## Deviations from the original milestone text

- The provider set is intentionally milestone-scoped. Milestone 5 supports only `DirectMl` and `Cpu`; that is a planning rule for this milestone, not the permanent provider architecture.
- The machine-local cache index is treated as the source of truth for planner-visible installed models in Milestone 5 only. This does not replace future project-local runtime provenance.
- No CLI or UI path was added for this milestone. The planner remains an internal slice with test-heavy validation.

## Risks closed by Milestone 5

- Stage execution logic reaching directly into manifest data and local cache state without a stable planner boundary.
- Implicit GPU readiness based only on provider discovery.
- Planner results that would require string matching or absolute-path parsing in later diagnostics.
- Milestone 6 assuming commercial-safe ASR was already solved.

## Remaining risks intentionally deferred

- Real ONNX execution validation in the planner.
- Additional providers beyond `DirectMl` and `Cpu`.
- Runtime-plan persistence into stage-run records and exported diagnostics.
- Broader stage coverage such as translation, TTS, diarization, and separation.

## Exit recommendation

Milestone 5 can be treated as closed. Milestone 6 should build on the planner boundary created here rather than bypassing it.

Next steps should:

1. Consume `StageRuntimePlan` from stage execution paths instead of hardcoding model/provider choices.
2. Record runtime-plan provenance with stage runs without changing the planner’s stable external shape.
3. Keep commercial-safe ASR explicitly blocked until a safe ASR baseline is introduced, instead of weakening the commercial-safe rules.
