#Requires -Version 7.4
<#
.SYNOPSIS
  Minimized -> global hotkey -> restored, docked, foreground, keyboard focus. Both edges.

.DESCRIPTION
  For each edge: a fresh isolated data root, Noto started by the shell with that edge stored, the target app
  brought to the foreground by a real click and real typing, a negative control, Noto minimized, the target
  re-established, then Ctrl+Alt+Win+Space. Each trial records separately: WS_VISIBLE, minimized, cloaked,
  foreground window, keyboard focus, and the outer rectangle against the one Noto docked at.

  -Control marks a run against a build expected NOT to restore (for example main before the fix): its trials
  are reported as they are, and the run passes when every activation check fails and every control is valid.

.EXAMPLE
  pwsh -File tools/validation/Invoke-MinimizedActivation.ps1 -NotoExe C:\builds\pr82\Noto.exe
#>
param(
    [Parameter(Mandatory)] [string] $NotoExe,
    [ValidateSet('Left', 'Right')] [string[]] $Edge = @('Right', 'Left'),
    [ValidateRange(1, 10)] [int] $Trials = 3,
    [switch] $Control
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun ($(if ($Control) { 'minimized-control' } else { 'minimized' }))
Write-Host "run $($run.Id)  exe $NotoExe"

$title = "Minimized activation$(if ($Control) { ' (control build)' })"
exit (Invoke-IsolatedRun $run -Title $title -Body {
    Assert-ChordFree $run
    $shell = Get-ShellPid
    $target = Start-TargetApp $run
    $previous = @()

    foreach ($e in $Edge) {
        $data = Join-Path $run.Root "data-$e"
        New-Item -ItemType Directory $data | Out-Null

        # First start creates the database; the edge is stored while Noto is closed, then a fresh start reads it.
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
        Add-Result $run "$e setup: first start exited cleanly" (Stop-Noto $first) "pid $($first.Pid)"
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'workspace.edge' -Value $e

        $noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        Add-Result $run "$e launch: fresh pid, started by the shell (parent $shell)" (($noto.Pid -ne $first.Pid) -and ($previous -notcontains $noto.Pid) -and $noto.Parent -eq $shell) "pid $($noto.Pid) parent $($noto.Parent)"
        $previous += $first.Pid, $noto.Pid

        $docked = Get-WindowEvidence $noto
        Add-Result $run "$e launch: docked flush to the $e edge of the work area" (Test-DockedAt $docked $e) "outer $($docked.Outer) frame $($docked.Frame) work $($docked.Work)"

        for ($i = 1; $i -le $Trials; $i++) {
            $token = Set-ForegroundByKeyboard $target
            $minimized = Invoke-Minimize $noto
            $m = Get-WindowEvidence $noto
            Add-Result $run "$e #$i minimized (iconic, Noto not in front)" ($minimized -and $m.Minimized -and -not $m.IsForeground) "outer $($m.Outer)"

            $token2 = Set-ForegroundByKeyboard $target
            $before = Get-WindowEvidence $noto
            if ($before.ForegroundPid -ne $target.Pid) { Add-Invalid $run "$e #$i the target was not in front before the chord" }
            $neg = Assert-ForegroundLocked $run $noto.Hwnd $target.Hwnd "$e #$i before"

            Send-ActivationChord -AllowedForeground $target.Hwnd
            $a = Get-WindowEvidence $noto
            $post = Assert-ForegroundLocked $run $noto.Hwnd $target.Hwnd "$e #$i after"

            $restored = $a.WsVisible -and -not $a.Minimized -and $a.Cloaked -eq 0
            $front = $a.IsForeground -and $a.KeyboardFocus
            $dock = ($a.Outer -eq $docked.Outer) -and (Test-DockedAt $a $e)
            $detail = "visible=$($a.WsVisible) minimized=$($a.Minimized) cloaked=$($a.Cloaked) fg=$($a.IsForeground)(pid $($a.ForegroundPid)) focus=$($a.KeyboardFocus)(pid $($a.FocusRootPid)) outer=$($a.Outer) expected=$($docked.Outer); typed $token,$token2; controls: before [$($neg.Detail)] after [$($post.Detail)]"

            if ($Control) {
                Add-Result $run "$e #$i control build does NOT restore+dock+focus" (-not ($restored -and $front -and $dock)) $detail
            }
            else {
                Add-Result $run "$e #$i restored (WS_VISIBLE, not minimized, not cloaked)" $restored $detail
                Add-Result $run "$e #$i foreground window with keyboard focus" $front ''
                Add-Result $run "$e #$i exact docked rectangle, flush to the $e edge" $dock ''
            }

            # Leave Noto restored for the next trial even when a control build did not restore it.
            if ([NotoVal.Win32]::IsIconic($noto.Hwnd)) { [void][NotoVal.Win32]::SendMessage($noto.Hwnd, 0x0112, [IntPtr]0xF120, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }
        }

        Add-Result $run "$e teardown: Noto exited cleanly" (Stop-Noto $noto) "pid $($noto.Pid)"
    }
})
