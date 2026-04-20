# Milestone 1 Completion Note

- Milestone: 1
- Title: Runtime viability harness
- Status: Complete
- Date: 2026-04-19

## Summary

Milestone 1 is complete. Babel Studio now has a runnable .NET benchmark harness that can load ONNX models from C#, select or report the execution provider, measure cold-load and warm inference timing, emit JSON reports, and surface failures without hiding them.

The main outcome of this milestone is not application UX. The outcome is a repeatable runtime proof that ONNX model loading and inference work through the intended C# path before the real editor is built.

## Completed in this milestone

- `src/BabelStudio.Benchmarks/` provides the command-line harness entry point, option parsing, console output, and JSON report writing.
- `src/BabelStudio.Inference/` and `src/BabelStudio.Inference.Onnx/` provide the benchmark runner, provider routing, ONNX session creation, and model-specific dummy input handling.
- `src/BabelStudio.Domain/` contains the benchmark request/report contracts.
- The harness reports:
  - requested and selected provider
  - cold load time
  - warmup time
  - warm latency average, minimum, and maximum
  - real-time factor where applicable
  - failure reason and notes
- The harness runs successfully against:
  - Silero VAD
  - Whisper Tiny ONNX encoder/decoder
  - one Opus-MT ONNX pair
- DirectML routing is wired and validated on the local machine.
- Variant-aware model selection is now supported through:
  - manifest-backed aliases such as `silero-vad`
  - explicit variant selection such as `silero-vad@q4`
  - `--all-variants` batch benchmarking
  - machine-local remembered defaults for ambiguous variant-only directories
- Benchmark tests cover formatting, provider reporting, resolver behavior, manifest-backed aliasing, Opus structure, ambiguous variant directories, and aggregate variant runs.

## Acceptance criteria assessment

### Harness runs from command line

Met. `BabelStudio.Benchmarks` runs from the CLI and writes JSON reports plus console summaries.

### At least one model loads and runs from C#

Met. Silero VAD loads and executes successfully from the .NET harness. Additional successful runs also exist for Whisper Tiny and Opus-MT.

### Harness reports provider, load time, inference time, and failure reason

Met. The benchmark report and console summary include requested/selected provider, cold load timing, warm timing, and failure reason when execution fails.

### Failed model/provider combinations are reported clearly, not swallowed

Met. Provider failures and resolver failures produce failed benchmark reports with explicit reasons instead of silent fallback or swallowed exceptions.

### No UI project exists yet

Met in practice. `src/BabelStudio.App/` currently contains placeholder directory READMEs only and no app project or implemented UI path.

## Deviations from the original milestone text

- The milestone prompt described writing `benchmark-report.json`; the harness now keeps that as the default output but also supports explicit per-model and per-variant report paths, which is better for real benchmarking work.
- A small amount of Milestone 2 groundwork was started early:
  - bundled model manifest registry
  - alias-based model resolution
  - variant metadata for benchmark selection

That scope creep is acceptable because it directly simplified benchmark execution and did not introduce UI, persistence, or runtime-planning layers beyond what the harness needed.

## Risks closed by Milestone 1

- Uncertainty about whether ONNX models can be loaded and executed from C# on the intended Windows runtime path.
- Uncertainty about CPU vs DirectML reporting behavior in the local harness.
- Fragility around model-path-only invocation for bundled models and their variants.
- Missing failure visibility for bad model paths, ambiguous model directories, and unavailable provider paths.

## Remaining risks intentionally deferred

- Commercial-safe evaluation and full manifest enforcement policy.
- Automatic model downloading and hash verification workflows.
- Runtime planning and provider/variant selection beyond benchmark-time choice.
- End-user UI for model selection, diagnostics, and benchmarking.
- Full media pipeline integration, project persistence, and transcript-first workflow validation.

## Exit recommendation

Milestone 1 can be treated as closed. The next work should move into Milestone 2 and stay narrow:

1. Formalize the model manifest layer beyond benchmark-only alias resolution.
2. Add validation and commercial-safe evaluation rules.
3. Keep benchmark execution working as the proving ground while manifest logic is hardened.

Do not treat the presence of benchmark manifests as completion of Milestone 2 by itself. The policy and validation layer still need to be implemented as first-class runtime behavior.
