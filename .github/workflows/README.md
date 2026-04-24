# Workflows

Add CI workflows here.

## Current workflows

- **`windows-build.yml`** — builds `BabelStudio.sln` and runs the full test
  suite on `windows-latest` (Release configuration). This is the only
  environment where the WinUI packaging path (MakePri / PRI / .appx) runs, so
  it's the only place where XAML codegen warnings can surface under
  `TreatWarningsAsErrors=true`. Linux builds succeed with 0 warnings today but
  skip packaging — treat the Windows job as the enforcement point.
- **`jekyll-gh-pages.yml`** — publishes `docs/` to GitHub Pages.

## Recommended future workflows

- run formatting/analyzer checks (`dotnet format --verify-no-changes`)
- package smoke build (generate .msix + assert it's signed/valid)
- Linux-only `dotnet build BabelStudio.sln -p:EnableWindowsTargeting=true` for
  faster pre-Windows-job feedback

Do not put secrets in workflow files. Use GitHub Actions secrets.
