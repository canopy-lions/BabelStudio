# Milestone 8 Completion

## Summary

Milestone 8 is complete as a gap-closure pass on the current BabelStudio workstation shell.

This slice keeps `MediaPlayerElement` and Media Foundation as the native happy path while closing the remaining playback/editor/project-management gaps:

- hybrid playback seam with capability probing before play
- explicit unsupported-format reporting when a fallback backend would be required
- runtime `MediaFailed` warning surfacing for Media Foundation playback failures
- waveform layout extracted into a pure helper for unit testing
- segment split, merge, and numeric trim flows preserved as revisioned transcript edits
- settings persistence and recent-project management through `%LocalAppData%\BabelStudio\settings.json`
- source relocation flow that updates both artifact metadata and the stored `MediaAsset` path

## Implemented behavior

### Playback shell

- `PlaybackCapabilityProbe` chooses `PlaybackBackendKind` before playback starts.
- `PlaybackService` now honors the selected backend instead of silently routing every source through Media Foundation.
- `DefaultPlaybackBackendFactory` only provides `MediaFoundationPlaybackBackend` for `PlaybackBackendKind.MediaFoundation`.
- If probe results require `FfmpegFallback` or `LibMpvFallback`, playback is left unavailable and the UI shows an explicit warning that the required fallback backend is not implemented in this build.
- `MediaFoundationPlaybackBackend` tracks open/playback failures through `MediaFailed` and reports them back through playback state so the UI can surface a truthful warning even for files that probed as likely supported.

### Waveform and segment editor

- Waveform math is now separated from Win2D drawing through a pure layout helper.
- The WinUI shell continues to support:
  - waveform click-to-seek
  - transcript click-to-seek
  - active segment highlight
  - source subtitle overlay
  - split at playback cursor
  - merge two adjacent selected segments
  - numeric trim with overlap rejection
- Segment edits continue to create new transcript revisions instead of mutating prior revisions in place.
- Transcript timing and text edits continue to mark downstream translation state as stale.

### Project management

- `JsonStudioSettingsService` remains the single persisted store for app-level settings and recent projects.
- Recent projects remain capped at 10, newest first.
- Opening a project with a missing source still prompts for relocation, validates the replacement by fingerprint, rewrites `media/source-reference.json`, updates `MediaAsset.SourceFilePath`, and then reopens playback against the relocated source.

### Test doubles

- Shared M8 test doubles now live under `tests/BabelStudio.TestDoubles/`.
- Playback tests reuse shared fake playback backend/factory doubles.
- Settings/recent-project tests have a shared in-memory `FakeStudioSettingsService` for non-I/O test scenarios.

## Non-goals still deferred

- No custom video renderer
- No FFmpeg playback backend yet
- No libmpv playback backend yet
- No drag-handle waveform editor
- No per-word alignment visualization
- No M9 translation-routing expansion

## Validation commands

Recommended validation after this milestone:

```powershell
dotnet build BabelStudio.sln -m:1 -p:Platform=x64
dotnet test tests/BabelStudio.Media.Tests\BabelStudio.Media.Tests.csproj -m:1 -p:Platform=x64
dotnet test tests/BabelStudio.Application.Tests\BabelStudio.Application.Tests.csproj -m:1 -p:Platform=x64
dotnet test tests/BabelStudio.Infrastructure.Tests\BabelStudio.Infrastructure.Tests.csproj -m:1 -p:Platform=x64
```

## Explicit deferred backend note

Milestone 8 ends with Media Foundation as the only implemented playback backend.

`PlaybackBackendKind.FfmpegFallback` and `PlaybackBackendKind.LibMpvFallback` are intentionally present in the seam, but real FFmpeg/libmpv playback implementation remains future work.
