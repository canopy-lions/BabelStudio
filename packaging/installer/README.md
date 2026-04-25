# packaging/installer

## Purpose

Installer packaging.

## What belongs here

WiX/Inno/installer scripts.

## Bundled eSpeak-NG path

M11 TTS uses the `espeak-ng.exe` subprocess for Kokoro grapheme-to-phoneme
conversion. Installer output should place the executable in one of the paths
resolved by `EspeakNgPathResolver`, preferably:

```text
runtimes/win-x64/native/espeak-ng/espeak-ng.exe
```

The binary remains a separate process. Keep the eSpeak-NG GPL license notice
and source-offer disclosure with the installer materials.

## What should not go here

Application logic.

## Agent guidance

Keep changes scoped to this directory's purpose. If a task requires crossing boundaries, update the relevant architecture note or ADR first.
