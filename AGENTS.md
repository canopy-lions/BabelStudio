# Repository Guidelines

## Project Structure & Module Organization

Windows-native, local-first AI dubbing workstation. .NET 10 / C# / WinUI 3 (planned).

**Strict dependency direction — Domain depends on nothing:**

```
App → Application → Domain ← Contracts
Infrastructure → Application → Domain
Infrastructure → Contracts
Media → Application → Domain
Media → Contracts
Inference → Domain, Contracts
Inference.Onnx → Inference, Domain, Contracts
Benchmarks → Inference, Inference.Onnx, Infrastructure, Domain
```

Projects currently scaffolded: `BabelStudio.Domain`, `BabelStudio.Inference`, `BabelStudio.Inference.Onnx`, `BabelStudio.Benchmarks`, `BabelStudio.Benchmarks.Tests`. Everything else (App, Application, Infrastructure, Media, Contracts, Tools) is planned.

Bundled ONNX models live under `models/` (whisper-tiny, kokoro-onnx, chatterbox-turbo-onnx, silero-vad, opus/Helsinki-NLP-opus-mt-en-es). Each must have a manifest entry before use.

## Build, Test, and Development Commands

```sh
dotnet build BabelStudio.sln
dotnet run --project src/BabelStudio.Benchmarks
dotnet test
dotnet test tests/BabelStudio.Benchmarks.Tests
```

Packages are centrally versioned in `Directory.Build.props` / `Directory.Packages.props`. Add package versions there, not per-project.

## Coding Style & Naming Conventions

- Target: `net10.0`, `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=enable`
- No linter config present — follow existing file conventions
- `TreatWarningsAsErrors=false` but nullable is enforced; don't suppress nullable warnings without justification

## Testing Guidelines

xunit 2.9.3. Run with `dotnet test`. Domain tests must be fast and pure (no I/O). Application tests use fakes. Infrastructure tests may use temporary SQLite files. Keep ONNX smoke tests separate from slow benchmarks.

## Commit & Pull Request Guidelines

Imperative short title: `Add ...`, `Remove ...`, `Fix ...`, `Revise ...`. PR template requires: Summary, Scope, Tests, and a license/model impact checklist.

## Marketing Tooling

`tools/marketing/` is a Python 3.11+ developer tooling directory for GitHub social previews, screenshots, and brand assets. It is **never shipped to end users**.

```sh
cd tools/marketing
python -m venv .venv && .venv/Scripts/activate
pip install -e .
playwright install chromium
# Set GEMINI_API_KEY in environment before running generate_image.py
python scripts/screenshot.py templates/social-preview.html --output output/social-preview.png --width 1280 --height 640
python scripts/generate_image.py "prompt" --output output/bg.png --aspect 16:9
```

## Agent Rules

1. Do not build the full app before model/runtime viability is proven via benchmarks.
2. Do not add Python, Conda, Docker, WSL, or CUDA Toolkit to the end-user runtime path. Exception: `tools/marketing/` is dev-only tooling and may use Python.
3. Do not add a model without a license manifest entry.
4. Do not place inference code in the WinUI project.
5. Do not place persistence code in view models.
6. Do not mutate project state from model wrappers.
7. Do not hide CPU/GPU/cloud route changes from the user.
8. Do not invent commercial safety — unknown license = unsafe.
9. Do not create plugin systems before the transcript-only vertical slice works.
10. Do not use non-commercial models in commercial-safe mode.

**Prefer bounded agent tasks:** one migration, one repository, one model wrapper, one benchmark scenario. Never "build the whole app."
