#Requires -Version 7.4
<#
.SYNOPSIS
  The #16 slice 4 global-hotkey campaign, with foreground rights controlled.

.DESCRIPTION
  Cases (one isolated data root, four fresh shell-started Noto processes):
    A  another app in front -> chord -> Noto foreground with keyboard focus (x Trials)
    B  Noto already in front and focused -> chord -> hidden (slice 5's toggle; slice 4 kept it shown)
    C  Noto was in front, another app took the foreground -> chord -> Noto regains it
    D  hotkey disabled -> chord not registered; pressing it leaves the other app in front
    E  another process holds the chord -> Noto still starts and docks; the chord stays the holder's
    F  malformed stored binding -> the default chord is registered and works; the stored value is untouched
    H  hygiene: chord held while running and freed on exit, settings rows unchanged by every run,
       every Noto pid fresh and shell-started, a negative control before and after every chord

.EXAMPLE
  pwsh -File tools/validation/Invoke-Slice4Foreground.ps1 -NotoExe C:\builds\main\Noto.exe
#>
param(
    [Parameter(Mandatory)] [string] $NotoExe,
    [ValidateRange(1, 10)] [int] $Trials = 3
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'slice4'
Write-Host "run $($run.Id)  exe $NotoExe"

exit (Invoke-IsolatedRun $run -Title 'Slice 4 global hotkey: foreground campaign' -Body {
    Assert-ChordFree $run
    $shell = Get-ShellPid
    $data = Join-Path $run.Root 'data'
    New-Item -ItemType Directory $data | Out-Null
    $target = Start-TargetApp $run
    $pids = [System.Collections.Generic.List[int]]::new()

    function Launch([string] $label) {
        $n = Start-Noto $run -Exe $NotoExe -DataRoot $data
        Add-Result $run "$label launch: fresh pid, started by the shell" (($pids -notcontains $n.Pid) -and $n.Parent -eq $shell) "pid $($n.Pid) parent $($n.Parent)"
        $pids.Add($n.Pid)
        $ev = Get-WindowEvidence $n
        Add-Result $run "$label launch: docked flush to the Right edge" (Test-DockedAt $ev 'Right') "outer $($ev.Outer) work $($ev.Work)"
        $n
    }

    # Brings the target forward with real input, sends the chord between two negative controls, measures.
    function Press-FromTarget($noto, [string] $label) {
        $null = Set-ForegroundByKeyboard $target
        $b = Get-WindowEvidence $noto
        if ($b.ForegroundPid -ne $target.Pid) { Add-Invalid $run "$label the target was not in front before the chord" }
        $null = Assert-ForegroundLocked $run $noto.Hwnd $target.Hwnd "$label before"
        Send-ActivationChord -AllowedForeground $target.Hwnd
        $a = Get-WindowEvidence $noto
        $null = Assert-ForegroundLocked $run $noto.Hwnd $target.Hwnd "$label after"
        $a
    }
    function Show([object] $a) { "fg pid=$($a.ForegroundPid) notoFg=$($a.IsForeground) focus=$($a.KeyboardFocus) visible=$($a.WsVisible) minimized=$($a.Minimized) outer=$($a.Outer)" }

    # ---------------------------------------------------------------- default binding, fresh database
    $noto = Launch 'A'
    $rows = Get-NotoSettings $run -Exe $NotoExe -DataRoot $data
    Add-Result $run 'H  chord held by Noto while it runs (a probe cannot register it)' (-not (Test-ChordFree)) ''
    for ($i = 1; $i -le $Trials; $i++) {
        $a = Press-FromTarget $noto "A #$i"
        Add-Result $run "A #$i other app in front -> Noto foreground with keyboard focus" ($a.IsForeground -and $a.KeyboardFocus) (Show $a)
    }

    $b0 = Get-WindowEvidence $noto
    if (-not ($b0.IsForeground -and $b0.KeyboardFocus)) { Add-Invalid $run 'B precondition: Noto is not in front and focused' }
    $null = Assert-ForegroundLocked $run $noto.Hwnd $target.Hwnd 'B before'
    Send-ActivationChord -AllowedForeground $noto.Hwnd
    Start-Sleep -Milliseconds 500
    $b1 = Get-WindowEvidence $noto
    Add-Result $run 'B  already in front and focused -> chord -> hidden (slice 5 toggle)' ((-not $b1.WsVisible) -and [bool](Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue)) "$($b0.Outer) -> $(Show $b1)"

    # Shown again for C, which starts from Noto shown behind another app. Not a claim, so not bracketed.
    $null = Set-ForegroundByKeyboard $target
    Send-ActivationChord -AllowedForeground $target.Hwnd

    $c = Press-FromTarget $noto 'C'
    Add-Result $run 'C  was in front, other app took the foreground -> Noto regains it with focus' ($c.IsForeground -and $c.KeyboardFocus) (Show $c)

    Add-Result $run 'H  default run: Noto exited cleanly' (Stop-Noto $noto) ''
    Add-Result $run 'H  exit frees the chord immediately' (Test-ChordFree) ''
    Add-Result $run 'H  default run changed no settings rows' ((Get-NotoSettings $run -Exe $NotoExe -DataRoot $data) -eq $rows) $rows

    # ---------------------------------------------------------------- disabled
    Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'activation.hotkey.enabled' -Value 'False'
    $rows = Get-NotoSettings $run -Exe $NotoExe -DataRoot $data
    $noto = Launch 'D'
    Add-Result $run 'D  disabled: chord not registered (a probe can take it while Noto runs)' (Test-ChordFree) ''
    $d = Press-FromTarget $noto 'D'
    Add-Result $run 'D  disabled: the chord leaves the other app in front' (-not $d.IsForeground -and $d.ForegroundPid -eq $target.Pid) (Show $d)
    Add-Result $run 'H  disabled run: Noto exited cleanly' (Stop-Noto $noto) ''
    Add-Result $run 'H  disabled run changed no settings rows' ((Get-NotoSettings $run -Exe $NotoExe -DataRoot $data) -eq $rows) $rows

    # ---------------------------------------------------------------- malformed binding
    Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'activation.hotkey.enabled' -Value 'True'
    Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'activation.hotkey.binding' -Value 'Ctrl+Hyper+Q'
    $rows = Get-NotoSettings $run -Exe $NotoExe -DataRoot $data
    $noto = Launch 'F'
    Add-Result $run 'F  malformed binding: the default chord is registered' (-not (Test-ChordFree)) ''
    $f = Press-FromTarget $noto 'F'
    Add-Result $run 'F  malformed binding: the default chord brings Noto forward with focus' ($f.IsForeground -and $f.KeyboardFocus) (Show $f)
    Add-Result $run 'H  malformed run: Noto exited cleanly' (Stop-Noto $noto) ''
    $after = Get-NotoSettings $run -Exe $NotoExe -DataRoot $data
    Add-Result $run 'F  malformed stored value left exactly as it was' (($after | ConvertFrom-Json).'activation.hotkey.binding' -eq 'Ctrl+Hyper+Q' -and $after -eq $rows) $after

    # ---------------------------------------------------------------- another process holds the chord
    Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'activation.hotkey.binding' -Value 'Ctrl+Alt+Win+Space'
    $rows = Get-NotoSettings $run -Exe $NotoExe -DataRoot $data
    $holder = Start-HotkeyHolder $run
    $held = (Get-Content $holder.Ready -ErrorAction SilentlyContinue) -join ' '
    Add-Result $run 'E  another process holds the chord' ($held -match 'held=True') $held
    $noto = Launch 'E'
    Add-Result $run 'E  Noto still starts and runs' ([bool](Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue)) ''
    $e = Press-FromTarget $noto 'E'
    Add-Result $run 'E  the holder keeps the chord: Noto does not come forward' (-not $e.IsForeground -and $e.ForegroundPid -eq $target.Pid) (Show $e)
    Add-Result $run 'E  Noto exited cleanly after the conflict' (Stop-Noto $noto) ''
    Add-Result $run 'H  conflict run changed no settings rows' ((Get-NotoSettings $run -Exe $NotoExe -DataRoot $data) -eq $rows) $rows
    $report = Stop-HotkeyHolder $holder
    Add-Result $run 'E  the holder received the press Noto did not take' ($report -match 'hits=[1-9]') $report

    Add-Result $run 'H  every Noto launch had a distinct pid' (@($pids | Select-Object -Unique).Count -eq $pids.Count -and $pids.Count -eq 4) ($pids -join ',')
})
