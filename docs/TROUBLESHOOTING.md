# Troubleshooting

## Transcript app startup

If `BabelStudio.App` builds but no window appears, check these two things first:

1. `ffmpeg` and `ffprobe` must resolve on `PATH`.

```powershell
where ffmpeg
where ffprobe
```

2. On some Windows machines, Nahimic / A-Volute overlay processes can crash WinUI startup before the window appears.

```powershell
Get-Process NahimicSvc32, NahimicSvc64 -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet run --project D:\Dev\BabelStudio\src\BabelStudio.App\BabelStudio.App.csproj
```

If the app opens after killing those processes, report that as an environment issue, not as a transcript-pipeline bug.

## Very short bug report template

When something fails, send exactly these five facts:

```text
1. Command used:
   dotnet run --project D:\Dev\BabelStudio\src\BabelStudio.App\BabelStudio.App.csproj

2. What I clicked:
   Open Media / Open Project / Save Transcript / app failed before window opened

3. Exact visible error:
   paste the full Status text, dialog text, or crash symptom

4. Input used:
   media file path or project folder path

5. Environment check:
   ffmpeg=yes/no, ffprobe=yes/no, Nahimic killed=yes/no
```

That is enough to separate:

- startup/runtime-environment failures
- ingest/tooling failures
- transcript-stage failures
- save/reopen persistence failures
