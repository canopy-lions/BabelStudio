# src/BabelStudio.Inference/Runtime/Planning

## Purpose

Milestone 5 runtime planning only.

## Scope boundaries

- `RuntimePlanner` lives here.
- The milestone-limited provider set stays here as `SupportedProvidersThisMilestone = { DirectMl, Cpu }`.
- Planner output is a stable diagnostics/logging shape and must not expose absolute machine-local paths.
- Smoke-test execution stays interface-driven in this milestone. No real ONNX session loading belongs here yet.

## Expectations

- Reuse `BundledModelManifestRegistry` and `CommercialSafeEvaluator` as the source of model metadata truth.
- Treat preferred aliases as soft ranking hints inside `StageRuntimeRequirements`.
- Commercial-safe ASR blocking with the current manifest inventory is expected in Milestone 5 and is covered by planner tests.
