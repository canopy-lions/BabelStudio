# AGENTS.md

Rules for Codex, Claude Code, and other AI coding agents.

## Core rules

1. Do not build the full app before model/runtime viability is proven.
2. Do not add Python, Conda, Docker, WSL, or CUDA Toolkit requirements to the end-user runtime path.
3. Do not add a model without a license manifest entry.
4. Do not place inference code in the WinUI project.
5. Do not place persistence code in view models.
6. Do not mutate project state from model wrappers.
7. Do not hide CPU/GPU/cloud route changes from the user.
8. Do not invent commercial safety. If license is unknown, mark it unsafe.
9. Do not create speculative plugin systems before the transcript-only vertical slice works.
10. Do not use non-commercial models in commercial-safe mode.

## Recommended agent workflow

Use agents for bounded tasks:

- Harness spike
- SQLite migration
- model manifest parser
- media probe
- one ONNX model wrapper
- one application use case
- one unit test suite

Bad prompt:

> Build Babel Studio.

Good prompt:

> Implement `BabelStudio.Harness` as a .NET 10 console app that loads one ONNX model, reports provider, load time, warm latency, and writes one benchmark row to SQLite. Do not create UI.
