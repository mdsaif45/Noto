#Requires -Version 7.4
<#
.SYNOPSIS
  Slice 1.3 motion: the drawer's content entering and leaving, a hide reversed by a show, a surface entering in its
  direction, reduced motion, and the window never moving.

.DESCRIPTION
  One isolated data root seeded with a folder. Noto, the target app and every helper are started by the shell, on a
  temporary virtual desktop; the real data folders are checked unchanged. Chords reach Noto only while one of this
  run's windows is in front, and every foreground claim is bracketed by negative controls. Evidence: Noto's
  Noto.Motion trace lines (start, done, reversed, skipped; never content), the window's own geometry sampled through
  each transition, and its visibility.

  Windows' "Animation effects" setting is READ, never changed: the run proves whichever path the machine is on (the
  other is covered by the unit tests), and says which.

    1  show: the content enters (start, then done after about its 250 ms) or, with animation effects off, is shown at
       once (skipped); the window's frame is docked and identical at every sample through the transition
    2  hide: the content leaves first and the window is hidden only when it has (start, done hidden=True, then not
       visible); off, hidden at once with no exit
    3  a show during the hide reverses it: the window is never hidden, it is in front, and the stale completion hides
       nothing (animations on only)
    4  surfaces: entering a folder slides in going forward, Escape back going back

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Motion.ps1 -NotoExe <frozen Release>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
if (-not ('NotoVal.Spi' -as [type])) {
    Add-Type -Namespace NotoVal -Name Spi -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint ini);'
}
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'motion'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F0M1'

function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Ev { Get-WindowEvidence $noto }
function Trace-Mark { @($log.Lines($noto.Pid)).Count }
function Trace-Lines([int] $Mark) { @($log.Lines($noto.Pid) | Select-Object -Skip $Mark) }
function Trace-Since([int] $Mark) { (Trace-Lines $Mark) -join ' | ' }
function Until([scriptblock] $Condition, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 20 }
    [bool](& $Condition)
}
function Stamp([string] $Line) { if ($Line -match '^(\d\d:\d\d:\d\d\.\d\d\d) ') { [TimeSpan]::ParseExact($Matches[1], 'hh\:mm\:ss\.fff', $null) } }
function First([int] $Mark, [string] $Pattern) { Trace-Lines $Mark | Where-Object { $_ -match $Pattern } | Select-Object -First 1 }
function Animations-On { $on = $false; [void][NotoVal.Spi]::SystemParametersInfo(0x1042, 0, [ref]$on, 0); $on }   # SPI_GETCLIENTAREAANIMATION
function Chord-Now([IntPtr] $Allowed) {
    <# Ctrl+Alt+Win+Space with no wait after it, only while the intended window is in front. #>
    if ((Fg) -ne $Allowed) { throw 'refusing to send the chord: the foreground window is not the intended one' }
    $down = @($VK.CTRL, $VK.ALT, $VK.LWIN) | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }
    $up = @($VK.LWIN, $VK.ALT, $VK.CTRL) | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) }
    [NotoVal.Win32]::Keys(@($down) + @([NotoVal.Win32]::Vk($VK.SPACE, $false), [NotoVal.Win32]::Vk($VK.SPACE, $true)) + @($up))
}
function Ctrl([string] $Label, [IntPtr] $Away) {
    $neg = Test-NegativeControl $run $Away
    $run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $run "$Label negative control was granted the foreground: $($neg.Detail)" }
}
function Frames([int] $Count, [int] $EveryMs) { 1..$Count | ForEach-Object { $e = Ev; "$($e.WsVisible)|$($e.Frame)"; Start-Sleep -Milliseconds $EveryMs } }
function Send-ToNoto([int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $noto.Hwnd) { throw 'refusing keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 600
}

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title 'Slice 1.3: motion' -Body {
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $animations = Animations-On
        Write-Host "  Windows animation effects: $(if ($animations) { 'on' } else { 'off' }) (read, not changed)"

        $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
        if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Validation', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
        $script:target = Start-TargetApp $run
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data

        # Hidden first, so case 1 is a real show from hidden.
        $null = Set-ForegroundByKeyboard $target
        if (-not (Until { -not (Ev).WsVisible })) { throw 'Noto did not hide on losing activation' }

        # 1 - show
        $null = Set-ForegroundByKeyboard $target
        Ctrl '1 show before' $noto.Hwnd
        $m = Trace-Mark
        Chord-Now $target.Hwnd
        $frames = @(Frames 12 30)
        if (-not (Until { (Fg) -eq $noto.Hwnd })) { throw '1 - the chord did not bring Noto forward' }
        Ctrl '1 show after' $target.Hwnd
        $null = Until { (Trace-Since $m) -match 'done kind=enter|skipped kind=enter' } 3
        $visible = @($frames | Where-Object { $_ -like 'True|*' })
        $still = @($visible | Select-Object -Unique).Count -eq 1 -and (Test-DockedAt (Ev) 'Right')
        if ($animations) {
            $start = Stamp (First $m 'Noto\.Motion start kind=enter ms=250'); $done = Stamp (First $m 'Noto\.Motion done kind=enter')
            $took = if ($start -and $done) { ($done - $start).TotalMilliseconds } else { -1 }
            Add-Result $run '1  show: the content enters (start ms=250, then done)' ($took -ge 150 -and $took -le 2000) "took=$took ms; trace: $(Trace-Since $m)"
        }
        else {
            Add-Result $run '1  show, animation effects off: shown at once, no enter' ((Trace-Since $m) -match 'skipped kind=enter reason=animations-off' -and (Trace-Since $m) -notmatch 'start kind=enter') "trace: $(Trace-Since $m)"
        }
        Add-Result $run '1  the window never moves: docked, one frame rectangle at every visible sample' ($visible.Count -gt 0 -and $still) "samples=$($frames -join ' ; ')"

        # 2 - hide
        $m = Trace-Mark
        Chord-Now $noto.Hwnd
        Start-Sleep -Milliseconds 40
        $duringVisible = (Ev).WsVisible
        $hidden = Until { -not (Ev).WsVisible } 3
        if ($animations) {
            $lines = Trace-Lines $m
            $order = ([array]::IndexOf($lines, (First $m 'start kind=exit ms=167')) -ge 0) -and ([array]::IndexOf($lines, (First $m 'start kind=exit')) -lt [array]::IndexOf($lines, (First $m 'done kind=exit step=HideNow hidden=True')))
            Add-Result $run '2  hide: the content leaves first, the window is hidden only when it has' ($duringVisible -and $hidden -and $order) "visible 40 ms in=$duringVisible; trace: $(Trace-Since $m)"
        }
        else {
            Add-Result $run '2  hide, animation effects off: hidden at once, no exit' ($hidden -and (Trace-Since $m) -notmatch 'start kind=exit') "trace: $(Trace-Since $m)"
        }

        # 3 - reversal
        if ($animations) {
            $null = Set-ForegroundByKeyboard $target
            Ctrl '3 before' $noto.Hwnd
            Chord-Now $target.Hwnd
            if (-not (Until { (Fg) -eq $noto.Hwnd })) { throw '3 - the chord did not bring Noto forward' }
            Start-Sleep -Milliseconds 600
            $m = Trace-Mark
            Chord-Now $noto.Hwnd            # hide: the content starts leaving
            Start-Sleep -Milliseconds 50
            Chord-Now $noto.Hwnd            # show, while it leaves
            $neverHidden = -not ((Frames 15 30) -like 'False|*')
            Add-Result $run '3  a show during the hide reverses it: never hidden, in front, the late completion hides nothing' ($neverHidden -and (Fg) -eq $noto.Hwnd -and (Trace-Since $m) -match 'Noto\.Motion reversed' -and (Trace-Since $m) -notmatch 'hidden=True') "trace: $(Trace-Since $m)"
            Ctrl '3 after' $target.Hwnd
        }

        # 4 - surfaces
        if ((Fg) -ne $noto.Hwnd) {
            $null = Set-ForegroundByKeyboard $target; Chord-Now $target.Hwnd
            if (-not (Until { (Fg) -eq $noto.Hwnd })) { throw '4 - the chord did not bring Noto forward' }
        }
        $list = $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'FolderList'))
        $row = $list.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))
        $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus(); Start-Sleep -Milliseconds 300
        $m = Trace-Mark; Send-ToNoto @($VK.CTRL) 0x28; Send-ToNoto @() 0x1B
        $expected = if ($animations) { 'start kind=surface forward=True[\s\S]*start kind=surface forward=False' } else { '^(?![\s\S]*start kind=surface)' }
        Add-Result $run '4  surfaces: forward into a folder, back out of it' ((Trace-Since $m) -match $expected) "trace: $(Trace-Since $m)"

        Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $noto) ''
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
}
exit $code
