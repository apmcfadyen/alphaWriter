# UI snapshots

Screenshots of every workspace at several window sizes, taken from a Debug build running
against a throwaway sample library. They're useful for catching layout nitpicks (clipped
text, cramped columns) without clicking through the app by hand.

```powershell
# 1. Build a Debug copy somewhere other than bin/ (so it can run beside your own instance)
dotnet build alphaWriter/alphaWriter.csproj -f net10.0-windows10.0.19041.0 -p:OutDir=$env:TEMP\aw-snap-app\

# 2. Capture
./tools/ui-snapshots/snapshot.ps1 -Exe $env:TEMP\aw-snap-app\alphaWriter.exe -OutDir $env:TEMP\aw-shots
```

- **Your library is safe.** `ALPHAWRITER_DATA_DIR` points the app at a temporary folder,
  and sandboxed runs keep separate preferences. Downloaded NLP models are shared (read-only).
- **States** are set with `ALPHAWRITER_UI_STATE` (Debug builds only), so the script never
  moves the mouse. See `ViewModels/WriterViewModel.UiState.cs` for the keys; add states to
  the `-States` table in `snapshot.ps1`.
- **Sample content** (`make_sample_library.py`) deliberately includes long titles, many
  chapters, an Outline scene and prose that triggers editorial notes.
- Windows pop up briefly while capturing; they close themselves.
