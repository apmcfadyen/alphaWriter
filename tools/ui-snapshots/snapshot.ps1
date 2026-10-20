<#
.SYNOPSIS
    Launches a Debug build of alphaWriter against a sandbox library, puts it into named UI
    states, and saves a PNG of the window at each requested size.

.DESCRIPTION
    Isolation: ALPHAWRITER_DATA_DIR points the app at -DataDir, and sandboxed runs use
    separate preferences, so the author's real library and settings are never touched.
    The sample library is regenerated for every state so each capture starts from the
    same data.

    Navigation: ALPHAWRITER_UI_STATE (see WriterViewModel.UiState.cs) selects the
    workspace / scene / panel at start-up, so no mouse or keyboard input is synthesized.

    Capture: PrintWindow with PW_RENDERFULLCONTENT, which includes WinUI and WebView2
    content and works when the window is behind other windows.

.EXAMPLE
    ./snapshot.ps1 -Exe ..\..\out\alphaWriter.exe -OutDir .\shots
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $DataDir = (Join-Path $env:TEMP "alphawriter-snapshot-data"),
    [string[]] $Sizes = @("1024x700", "1366x768", "1920x1080"),
    [hashtable] $States = [ordered]@{
        "write"          = "book=first;scene=The Fog Rolls In;inspector=on;workspace=Write"
        "write-empty"    = "book=first;workspace=Write"
        "write-quotes"   = "book=first;scene=Straight Quotes in the Ledger;workspace=Write"
        "storybible"     = "book=first;character=Eleonora;workspace=StoryBible"
        "revise"         = "book=first;scene=The Fog Rolls In;scope=scene;workspace=Revise"
        "revise-settings"= "book=first;settings=on;workspace=Revise"
        "revise-station" = "book=first;scene=The Station at a Quarter Past Four;scope=scene;workspace=Revise"
        "revise-dev"     = "book=first;scene=The Fog Rolls In;scope=scene;workspace=Revise;filter=Developmental Editor"
        "insights"       = "book=first;insight=Problems;workspace=Insights"
        "insights-report"= "book=first;analyze=on;insight=report:NLP"
        "revise-passes"  = "book=first;analyze=on;workspace=Revise;passes=on"
        "revise-pass"    = "book=first;analyze=on;workspace=Revise;pass=style.show-dont-tell"
        "book"           = "book=first;workspace=Book"
    },
    [int] $StartupSeconds = 12,
    [int] $SettleSeconds = 2
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@

function Save-WindowPng([IntPtr] $hwnd, [string] $path) {
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($hwnd, [ref] $r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][Win]::PrintWindow($hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$generator = Join-Path $PSScriptRoot "make_sample_library.py"
$results = @()

foreach ($name in $States.Keys) {
    # Fresh sample data for every state
    if (Test-Path $DataDir) { Remove-Item -Recurse -Force $DataDir }
    python $generator $DataDir | Out-Null

    $env:ALPHAWRITER_DATA_DIR = $DataDir
    $env:ALPHAWRITER_UI_STATE = $States[$name]
    $proc = Start-Process -FilePath $Exe -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(30)
        while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250; $proc.Refresh()
        }
        if ($proc.MainWindowHandle -eq 0) { throw "No window for state '$name'" }
        $hwnd = $proc.MainWindowHandle
        [void][Win]::ShowWindow($hwnd, 9)   # SW_RESTORE (not maximized, so sizes apply)
        Start-Sleep -Seconds $StartupSeconds  # editor WebView + live analysis

        $scale = [Win]::GetDpiForWindow($hwnd) / 96.0
        foreach ($size in $Sizes) {
            $wl, $hl = $size.Split("x") | ForEach-Object { [int]$_ }
            [void][Win]::SetWindowPos($hwnd, [IntPtr]::Zero, 20, 20,
                [int]($wl * $scale), [int]($hl * $scale), 0x0014)  # NOZORDER | NOACTIVATE
            Start-Sleep -Seconds $SettleSeconds
            $file = Join-Path $OutDir "$name-$size.png"
            Save-WindowPng $hwnd $file
            $results += $file
        }
    }
    finally {
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
        Remove-Item Env:ALPHAWRITER_DATA_DIR, Env:ALPHAWRITER_UI_STATE -ErrorAction SilentlyContinue
    }
}

$results
