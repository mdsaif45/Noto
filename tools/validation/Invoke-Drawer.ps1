#Requires -Version 7.4
<#
.SYNOPSIS
  #16 slice 6: the drawer — topmost while shown, put away by Ctrl+W, Escape or losing activation, pin.

.DESCRIPTION
  One isolated data root seeded with a folder and a note. Noto, the target app, a bystander and a borderless
  full-screen window are started by the shell. Keys reach Noto only while it is verifiably in front; clicks
  only where the window under the point is the intended one. Every claim that Noto took the foreground is
  bracketed by a negative control; any grant makes the run INVALID. Noto's own Noto.Workspace diagnostics
  (each request and its action) are read from the debug-output channel.

  "Outside click" is parity's term (A13); the mechanism under test is workspace deactivation — activation moving
  to another process's window (WM_ACTIVATEAPP), by a click, a newly launched window, Alt+Tab, a desktop switch or
  the shell.

    1  launch -> docked, shown, topmost (WS_EX_TOPMOST)
    2  unpinned + a real click on another app -> hidden (request=Deactivated action=Hide)
    3  pinned + a click on a MAXIMIZED other app -> Noto stays shown, above it (WindowFromPoint), the other app in front
    4  a borderless full-screen window (normal, then itself topmost) started by the shell over a shown Noto ->
       external application activation hides Noto; the hotkey then shows Noto above it, in front
    5  hide-on-deactivation OFF in the store -> a click elsewhere leaves Noto shown, above the other app
    6  Escape, each of the 4 behaviours, on the editor, the note list and the folder list -> the A12 table;
       Escape in an inline input cancels the input and never hides
    7  Ctrl+W on each surface -> hidden; unsaved text saved once first; a failed save keeps Noto shown
    8  losing activation with unsaved text -> saved, then hidden; a failed save keeps Noto shown, focus not taken back
    9  an inner-edge resize dragged across another window -> not a deactivation; still shown, docked, wider
    10 the editor's own context menu (Noto's own window) -> not a deactivation; still shown
    11 virtual desktops: unpinned, a switch hides Noto and the hotkey shows it on the new desktop;
       pinned, Noto stays on its desktop and the hotkey makes Windows switch back
    12 the user acts in the shell (a desktop click) -> hidden; a second launch then shows it, in front
    13 the hotkey after an automatic hide -> shown; the hotkey on a pinned, focused Noto -> hidden (pin stops only deactivation)
    15 minimize -> not hidden (Deactivated -> None); the hotkey restores it, topmost
    16 a press on Noto's own taskbar button -> MEASURED: no deactivation, Noto stays shown (a deviation from the
       design gate's prediction of "hidden", accepted by the owner as measured)
    17 pin by keyboard (focus + Space); pin is never stored: a relaunch starts unpinned
    18 another application launched over a shown, unpinned Noto takes the foreground -> hidden, in each of 5 rounds
       (the shell-launch race PR #92 found: a deactivation judged before the foreground had changed was dropped)
    19 Alt+Tab, 5 rounds from a shown Noto and 2 from a hidden one: whatever Windows does, Noto ends either
       hidden and not active, or shown and in front. Hidden while foreground or holding keyboard focus is never
       accepted (the defect PR #92 found: the switcher took activation, Noto hid, the switch completed to Noto)
  Throughout: a hidden Noto that is the foreground window or holds keyboard focus is a FAIL wherever it is seen.
  Startup (a deactivation during launch never hides) is covered by unit tests: its timing cannot be forced here.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Drawer.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path

if (-not ('NotoVal.Drawer' -as [type])) {
    Add-Type -Namespace NotoVal -Name Drawer -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder name, int max);
'@
}

if (-not ('NotoVal.ForegroundChain' -as [type])) {
    # Every foreground change, sampled on its own thread while the harness waits on a launch: a console host is in
    # front for a few hundred milliseconds only. Read-only.
    Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text; using System.Threading;
namespace NotoVal {
public static class ForegroundChain {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder b, int n);
    static volatile bool stop; static Thread thread; static readonly List<KeyValuePair<uint, string>> seen = new List<KeyValuePair<uint, string>>();
    public static void Start() {
        stop = false; lock (seen) seen.Clear();
        thread = new Thread(() => { IntPtr last = new IntPtr(-1); while (!stop) { IntPtr h = GetForegroundWindow(); if (h != last) { last = h; uint p; GetWindowThreadProcessId(h, out p); var c = new StringBuilder(128); GetClassName(h, c, 128); lock (seen) seen.Add(new KeyValuePair<uint, string>(p, c.ToString())); } Thread.Sleep(2); } });
        thread.IsBackground = true; thread.Start();
    }
    public static KeyValuePair<uint, string>[] Stop() { stop = true; thread.Join(); lock (seen) return seen.ToArray(); }
}
}
'@
}

$run = New-ValidationRun 'drawer'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F006'
$NoteId = '01J0000000000000000000N006'
$Original = 'original note text'

# ---------------------------------------------------------------- helpers

function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Ev($Noto) { Get-WindowEvidence $Noto }
function Topmost([IntPtr] $Hwnd) { ([NotoVal.Drawer]::GetWindowLong($Hwnd, -20) -band 0x8) -ne 0 }
function Show-State($e) { "visible=$($e.WsVisible) minimized=$($e.Minimized) cloaked=$($e.Cloaked) fg=$($e.IsForeground)(pid $($e.ForegroundPid)) focus=$($e.KeyboardFocus) outer=$($e.Outer)" }
function Is-Shown-Docked($e) { $e.WsVisible -and -not $e.Minimized -and $e.Cloaked -eq 0 -and (Test-DockedAt $e 'Right') }
function Root-At([int] $X, [int] $Y) { $pt = New-Object NotoVal.Win32+POINT; $pt.X = $X; $pt.Y = $Y; [NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) }
function Above-At-Centre($Noto) { $e = Ev $Noto; (Root-At ([int](($e.FrameLeft + $e.FrameRight) / 2)) ([int](($e.FrameTop + $e.FrameBottom) / 2))) -eq $Noto.Hwnd }

function Never-Hidden-Active([string] $Label) {
    <# The invariant behind PR #92's second fix: a hidden Noto is never the foreground window nor holds keyboard focus. #>
    $e = Ev $noto
    $bad = (-not $e.WsVisible) -and ($e.IsForeground -or $e.KeyboardFocus)
    Add-Result $run "$Label -> never hidden and active" (-not $bad) "$(Show-State $e)"
}

function Send-ToTarget([int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $target.Hwnd) { throw 'refusing to send keys: the target app is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 700
}

function Locked([string] $Label, [IntPtr] $Other) { $null = Assert-ForegroundLocked $run -A $noto.Hwnd -B $Other -Label $Label }

function Locked-On([string] $Label, [IntPtr] $Away) {
    <# The negative control aimed at one named window — visible, in the background and unrelated to the claim. #>
    $neg = Test-NegativeControl $run $Away
    $run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $run "$Label negative control was granted the foreground: $($neg.Detail)" }
}

function Wait-Presence($Noto, [scriptblock] $Condition, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = Ev $Noto; if (& $Condition $e) { return $e }; Start-Sleep -Milliseconds 150 }
    Ev $Noto
}

function Chord([IntPtr[]] $Allowed) { Send-ActivationChord -AllowedForeground $Allowed }

function Send-ToNoto([int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 700
}

function Click-At([int] $X, [int] $Y, [IntPtr] $Expected, [switch] $Right) {
    if ((Root-At $X $Y) -ne $Expected) { throw "refusing to click at ($X,$Y): it is not the intended window" }
    [void][NotoVal.Win32]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 150
    if ((Root-At $X $Y) -ne $Expected) { throw "refusing to click at ($X,$Y): the window under it changed" }
    if ($Right) { [NotoVal.Drawer]::mouse_event(8, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Drawer]::mouse_event(16, 0, 0, 0, [IntPtr]::Zero) }
    else { [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 700
}

function Find-ById([string] $Id, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $e = $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, $cond)
        if ($e -and -not $e.Current.IsOffscreen) { return $e }
        Start-Sleep -Milliseconds 200
    }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Visible-Id([string] $Id) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $e = $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, $cond)
    [bool]($e -and -not $e.Current.IsOffscreen)
}
function Surface { if (Visible-Id 'NoteEditor') { 'Editor' } elseif (Visible-Id 'NoteList') { 'NoteList' } elseif (Visible-Id 'FolderList') { 'FolderList' } else { 'none' } }
function First-Item([string] $ListId) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $list = Find-ById $ListId; $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) { $i = $list.FindFirst($Scope::Descendants, $cond); if ($i) { return $i }; Start-Sleep -Milliseconds 200 }
    throw "list '$ListId' has no item"
}
function Select-AndFocus($Item) { $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $Item.SetFocus(); Start-Sleep -Milliseconds 300 }
function Editor-Value { (Find-ById 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Set-Editor([string] $Text) { (Find-ById 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text); Start-Sleep -Milliseconds 300 }
function Notice { try { (Find-ById 'EditorNoticeText' 2).Current.Name } catch { '' } }
function Row([string] $Column) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql "SELECT $Column FROM Notes WHERE Id = `$n" -Parameters @{ '$n' = $NoteId } }
function Pin-State { (Find-ById 'PinToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() }
function Pin-Click {
    $r = (Find-ById 'PinToggle').Current.BoundingRectangle
    Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) $noto.Hwnd
}

function Set-Pin([string] $Want) {
    <# Each scenario states the pin it needs, rather than inheriting one. #>
    Bring-NotoForward
    if ((Pin-State) -ne $Want) { Pin-Click }
    if ((Pin-State) -ne $Want) { throw "the pin did not become $Want" }
}

function Relock-Forward {
    <# Real input to the target, then the hotkey: Noto in front, with no foreground right left over from a shell click. #>
    $null = Set-ForegroundByKeyboard $target
    Chord @($target.Hwnd)
    $null = Wait-Presence $noto { param($e) $e.IsForeground }
    if ((Fg) -ne $noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}

function Trace-Mark { @($log.Lines($noto.Pid)).Count }
function Trace-Since([int] $Mark) { @($log.Lines($noto.Pid) | Select-Object -Skip $Mark) -join ' | ' }

function Bring-NotoForward {
    if ((Fg) -eq $noto.Hwnd) { return }
    $null = Set-ForegroundByKeyboard $target
    Chord @($target.Hwnd)
    $null = Wait-Presence $noto { param($e) $e.IsForeground }
    if ((Fg) -ne $noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}

function Open-Editor {
    Bring-NotoForward
    if ((Surface) -eq 'Editor') { return }
    if ((Surface) -eq 'NoteList') { Send-ToNoto @() 0x1B }
    if ((Surface) -eq 'NoteList') { (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500 }
    Select-AndFocus (First-Item 'FolderList'); Send-ToNoto @(0x11) 0x28
    Select-AndFocus (First-Item 'NoteList'); Send-ToNoto @() 0x0D
    $null = Find-ById 'NoteEditor'
}

function To-Surface([string] $Want) {
    # Reaches a surface without relying on Escape, whose behaviour is what is under test.
    Bring-NotoForward
    switch ($Want) {
        'Editor' { Open-Editor }
        'NoteList' {
            if ((Surface) -ne 'Editor') { Open-Editor }
            (Find-ById 'EditorBackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600
        }
        'FolderList' {
            if ((Surface) -eq 'Editor') { (Find-ById 'EditorBackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600 }
            if ((Surface) -eq 'NoteList') { (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600 }
            Select-AndFocus (First-Item 'FolderList')
        }
    }
    if ((Surface) -ne $Want) { throw "could not reach the $Want surface (on $(Surface))" }
}

function Restart-Noto([hashtable] $Settings) {
    if ($script:noto) { if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly before a restart' } }
    foreach ($k in $Settings.Keys) { Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key $k -Value $Settings[$k] }
    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
}

$taskbarTray = [NotoVal.Win32]::FindWindow('Shell_TrayWnd', [NullString]::Value)
function Noto-TaskbarButton {
    @($AE::FromHandle($taskbarTray).FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match '^Noto\b' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button }) | Select-Object -First 1
}

function Focus-Shell {
    $mi = New-Object NotoVal.Win32+MONITORINFO; $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
    [void][NotoVal.Win32]::GetMonitorInfo([NotoVal.Win32]::MonitorFromWindow($taskbarTray, 1), [ref]$mi)
    $w = $mi.rcWork
    foreach ($fx in 0.35, 0.5, 0.25) { foreach ($fy in 0.92, 0.8) {
        $x = [int]($w.Left + ($w.Right - $w.Left) * $fx); $y = [int]($w.Top + ($w.Bottom - $w.Top) * $fy)
        if (@('Progman', 'WorkerW') -notcontains [NotoVal.Instance]::RootClassAt($x, $y)) { continue }
        Click-At $x $y (Root-At $x $y)
        $fgPid = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId((Fg), [ref]$fgPid)
        if ($fgPid -eq (Get-ShellPid)) { return }
    } }
    throw 'could not make the shell the foreground with a desktop click'
}

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title '#16 slice 6: the drawer' -Body {
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $target = Start-TargetApp $run -Name 'target'
        $bystander = Start-TargetApp $run -Name 'bystander'
        $null = Set-ForegroundByKeyboard $bystander

        $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
        $seed = Start-Noto $run -Exe $NotoExe -DataRoot $data
        if (-not (Stop-Noto $seed)) { throw 'the seeding start did not exit cleanly' }
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Validation', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" -Parameters @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = $Original; '$t' = $now }

        $script:noto = $null
        Restart-Noto @{}

        # 1 - launch: docked, shown, topmost
        $e = Ev $noto
        Add-Result $run '1  launch -> docked, shown, topmost (WS_EX_TOPMOST)' ((Is-Shown-Docked $e) -and (Topmost $noto.Hwnd)) "$(Show-State $e) topmost=$(Topmost $noto.Hwnd)"
        Add-Result $run '1  ... the pin starts off' ((Pin-State) -eq 'Off') "pin=$(Pin-State)"

        # 2 - unpinned, a real click on another app -> hidden
        Bring-NotoForward
        $m = Trace-Mark
        $null = Set-ForegroundByKeyboard $target
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '2  unpinned + a real click on another app -> hidden' ((-not $e.WsVisible) -and (Fg) -eq $target.Hwnd) "$(Show-State $e); trace: $(Trace-Since $m)"
        Add-Result $run '2  ... decided by the coordinator as a deactivation' ((Trace-Since $m) -match 'request=Deactivated action=Hide') ''
        Never-Hidden-Active '2  after the click'

        # 13a - the hotkey after an automatic hide -> shown
        Locked '13a before' $bystander.Hwnd
        Chord @($target.Hwnd)
        $e = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
        Locked '13a after' $target.Hwnd
        Add-Result $run '13 the hotkey after an automatic hide -> shown, docked, in front, topmost' ((Is-Shown-Docked $e) -and $e.IsForeground -and (Topmost $noto.Hwnd)) (Show-State $e)

        # 3 - pinned + a click on a MAXIMIZED other app -> stays shown, above it
        Pin-Click
        Add-Result $run '3  the header pin toggles on' ((Pin-State) -eq 'On') "pin=$(Pin-State)"
        [void][NotoVal.Drawer]::ShowWindow($target.Hwnd, 3); Start-Sleep -Milliseconds 800     # SW_MAXIMIZE
        $m = Trace-Mark
        $null = Set-ForegroundByKeyboard $target
        Start-Sleep -Milliseconds 800
        $e = Ev $noto
        Add-Result $run '3  pinned + a click on a maximized app -> Noto stays shown, the other app in front' ((Is-Shown-Docked $e) -and (Fg) -eq $target.Hwnd) "$(Show-State $e); trace: $(Trace-Since $m)"
        Add-Result $run '3  ... and Noto is above the maximized app (topmost, WindowFromPoint)' ((Topmost $noto.Hwnd) -and (Above-At-Centre $noto)) ''
        Add-Result $run '3  ... the deactivation was judged and left it (Deactivated -> None)' ((Trace-Since $m) -match 'request=Deactivated action=None') ''

        # 13b - the hotkey on a pinned, focused Noto -> hidden (pin stops only deactivation)
        Chord @($target.Hwnd)
        $null = Wait-Presence $noto { param($e) $e.IsForeground }
        Chord @($noto.Hwnd)
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '13 the hotkey on a pinned, focused Noto -> hidden' (-not $e.WsVisible) (Show-State $e)
        [void][NotoVal.Drawer]::ShowWindow($target.Hwnd, 9); Start-Sleep -Milliseconds 600    # SW_RESTORE
        Set-Pin 'Off'

        # 4 - borderless full-screen windows started over a shown Noto: their activation hides Noto (A13), then
        # the hotkey shows Noto above them. The hide is asserted: before PR #92's fix it was a race that left Noto
        # shown behind the new window while this scenario stayed green.
        foreach ($top in $false, $true) {
            $name = if ($top) { 'fullscreen-topmost' } else { 'fullscreen' }
            $title = "Noto validation $name $($run.Id)"
            $pre = Ev $noto
            if (-not ($pre.WsVisible -and $pre.IsForeground)) { throw "4 $name precondition: Noto was not shown and in front before the launch ($(Show-State $pre))" }
            $m4 = Trace-Mark
            $arguments = "-NoProfile -WindowStyle Hidden -File `"$(Join-Path $PSScriptRoot 'helpers/fullscreen-app.ps1')`" -Title `"$title`"" + $(if ($top) { ' -TopMost' } else { '' })
            $fsProc = Start-ViaShell $run -File 'pwsh.exe' -Arguments $arguments -Marker $title
            $fs = [IntPtr]::Zero; $deadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $deadline -and $fs -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250; $fs = [NotoVal.Win32]::FindWindow([NullString]::Value, $title) }
            if ($fs -eq [IntPtr]::Zero) { throw "the $name window never appeared" }
            Start-Sleep -Milliseconds 800
            $how = 'took the foreground itself'
            if ((Fg) -ne $fs) { $how = 'put in front by a click'; Click-At 200 200 $fs }
            if ((Fg) -ne $fs) { throw "the $name window could not be put in front" }
            $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
            Add-Result $run "4  external application activation hides Noto: the $name window ($how) over a shown, unpinned Noto -> hidden" ((-not $e.WsVisible) -and (Trace-Since $m4) -match 'request=Deactivated action=Hide') "before: $(Show-State $pre); after: $(Show-State $e); trace: $(Trace-Since $m4)"
            # Aimed at the target app: Noto was hidden by the click on the full-screen window a moment ago, and a
            # window that has only just lost activation is not what this control is about.
            Locked-On "4 $name before" $target.Hwnd
            Chord @($fs)
            $e = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
            Locked-On "4 $name after" $target.Hwnd
            Add-Result $run "4  over a borderless $name window: the hotkey shows Noto in front, above it" ((Is-Shown-Docked $e) -and $e.IsForeground -and (Above-At-Centre $noto) -and (Topmost $noto.Hwnd)) "$(Show-State $e)"
            Stop-Process -Id $fsProc.Pid -Force; Start-Sleep -Milliseconds 600
        }

        # 10 - the editor's own context menu is not a deactivation
        Open-Editor
        $m = Trace-Mark
        $edRect = (Find-ById 'NoteEditor').Current.BoundingRectangle
        Click-At ([int]($edRect.X + $edRect.Width / 2)) ([int]($edRect.Y + 40)) $noto.Hwnd -Right
        Start-Sleep -Milliseconds 800
        $fgPid = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId((Fg), [ref]$fgPid)
        $e = Ev $noto
        Add-Result $run "10 the editor's own context menu -> not a deactivation: Noto stays shown" ($e.WsVisible -and $fgPid -eq $noto.Pid -and (Trace-Since $m) -notmatch 'action=Hide') "$(Show-State $e) foreground pid=$fgPid; trace: $(Trace-Since $m)"
        if ($fgPid -eq $noto.Pid) { [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x1B, $false), [NotoVal.Win32]::Vk(0x1B, $true))); Start-Sleep -Milliseconds 600 }

        # 7 - Ctrl+W on each surface; unsaved text saved once first
        Set-Pin 'Off'
        Open-Editor
        $updatedBefore = Row 'UpdatedAt'
        $text7 = "saved on ctrl+w $([guid]::NewGuid().ToString('N').Substring(0, 6))"
        Set-Editor $text7
        $m = Trace-Mark
        Send-ToNoto @(0x11) 0x57
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '7  Ctrl+W in the editor with unsaved text -> saved once, then hidden' ((-not $e.WsVisible) -and (Row 'Content') -eq $text7 -and (Row 'UpdatedAt') -ne $updatedBefore) "content='$(Row 'Content')'; trace: $(Trace-Since $m)"
        Add-Result $run '7  ... a dismissal, through the coordinator' ((Trace-Since $m) -match 'request=Dismiss action=Hide') ''
        foreach ($surface in 'NoteList', 'FolderList') {
            To-Surface $surface
            Send-ToNoto @(0x11) 0x57
            $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
            Add-Result $run "7  Ctrl+W on the $surface -> hidden" (-not $e.WsVisible) (Show-State $e)
        }

        # 7b / 8b - a failed save keeps Noto shown
        Open-Editor
        $text7b = "cannot be saved $([guid]::NewGuid().ToString('N').Substring(0, 6))"
        Set-Editor $text7b
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = `$t WHERE Id = `$n" -Parameters @{ '$t' = [DateTimeOffset]::UtcNow.ToString('O'); '$n' = $NoteId }
        Send-ToNoto @(0x11) 0x57
        Start-Sleep -Milliseconds 800
        $e = Ev $noto
        Add-Result $run '7  Ctrl+W whose save fails -> stays shown; text kept; notice shown; nothing written' ($e.WsVisible -and (Editor-Value) -eq $text7b -and (Notice) -match 'recycle bin' -and (Row 'Content') -eq $text7) "$(Show-State $e) notice='$(Notice)'"
        $null = Set-ForegroundByKeyboard $target
        Start-Sleep -Milliseconds 800
        $e = Ev $noto
        Add-Result $run '8  losing activation whose save fails -> stays shown; focus not taken back' ($e.WsVisible -and (Fg) -eq $target.Hwnd -and (Notice) -match 'recycle bin') "$(Show-State $e)"
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = NULL WHERE Id = `$n" -Parameters @{ '$n' = $NoteId }

        # 8 - losing activation with unsaved text -> saved, then hidden
        Set-Pin 'Off'
        Bring-NotoForward
        $text8 = "saved on deactivation $([guid]::NewGuid().ToString('N').Substring(0, 6))"
        Set-Editor $text8
        $m = Trace-Mark
        $null = Set-ForegroundByKeyboard $target
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '8  losing activation with unsaved text -> saved, then hidden' ((-not $e.WsVisible) -and (Row 'Content') -eq $text8) "content='$(Row 'Content')'; trace: $(Trace-Since $m)"

        # 9 - a resize dragged across another window is not a deactivation
        Bring-NotoForward
        To-Surface 'FolderList'
        $e9 = Ev $noto
        $x = $e9.FrameLeft - 3; $y = [int](($e9.FrameTop + $e9.FrameBottom) / 2)
        if ((Root-At $x $y) -ne $noto.Hwnd) { throw "refusing to drag at ($x,$y): it is not Noto's inner edge" }
        [void][NotoVal.Win32]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 200
        [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
        $tr = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($target.Hwnd, [ref]$tr)
        $endX = [Math]::Max($tr.Right - 40, $x - 400)
        for ($i = 1; $i -le 10; $i++) { [void][NotoVal.Win32]::SetCursorPos([int]($x - ($x - $endX) * $i / 10), $y); Start-Sleep -Milliseconds 60 }
        [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 900
        $e = Ev $noto
        Add-Result $run '9  an inner-edge resize dragged over another window -> still shown, docked, wider' ((Is-Shown-Docked $e) -and ($e.FrameRight - $e.FrameLeft) -gt ($e9.FrameRight - $e9.FrameLeft)) "before $($e9.Frame) after $($e.Frame)"

        # 15 - minimize is not a hide; the hotkey restores, topmost
        Set-Pin 'Off'
        Bring-NotoForward
        $m = Trace-Mark
        $null = Invoke-Minimize $noto
        Start-Sleep -Milliseconds 600
        $e = Ev $noto
        Add-Result $run '15 minimize -> minimized, not hidden (Deactivated -> None)' ($e.WsVisible -and $e.Minimized -and (Trace-Since $m) -notmatch 'action=Hide') "$(Show-State $e); trace: $(Trace-Since $m)"
        $null = Set-ForegroundByKeyboard $target
        Chord @($target.Hwnd)
        $e = Wait-Presence $noto { param($e) -not $e.Minimized -and $e.IsForeground }
        Add-Result $run '15 ... the hotkey restores it: docked, in front, topmost' ((Is-Shown-Docked $e) -and $e.IsForeground -and (Topmost $noto.Hwnd)) (Show-State $e)

        # 16 - a press on Noto's own taskbar button. MEASURED, and a deviation from the slice 6 design gate (which
        # predicted "hidden"): the press does not take activation from Noto, and Windows does not minimize a
        # window that is not minimizable — so nothing happens. Asserted as measured, labelled as a deviation.
        Set-Pin 'Off'
        Bring-NotoForward
        $button = Noto-TaskbarButton
        if (-not $button) { Add-Invalid $run '16 no taskbar button for Noto was found' }
        else {
            $r = $button.Current.BoundingRectangle
            $m = Trace-Mark
            Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) $taskbarTray
            Start-Sleep -Milliseconds 800
            $e = Ev $noto
            Add-Result $run "16 [DEVIATION from the design gate] a press on Noto's taskbar button -> no deactivation; stays shown, in front" ((Is-Shown-Docked $e) -and $e.IsForeground -and (Trace-Since $m) -notmatch 'request=Deactivated') "$(Show-State $e); trace: $(Trace-Since $m)"
        }

        # 12 - the user acts in the shell -> hidden; a second launch then shows it, in front.
        # The "before" control runs while Noto is in front: a probe started while the shell is in front would
        # inherit the shell's foreground right and prove nothing.
        Set-Pin 'Off'
        Relock-Forward
        Locked '12 before' $target.Hwnd
        Focus-Shell
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '12 a click on the desktop (the shell) -> hidden' (-not $e.WsVisible) (Show-State $e)
        Never-Hidden-Active '12 after the desktop click'
        $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
        $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`"", $run.Root, 'open', 1)
        $second = [NotoVal.Instance]::WaitNew('Noto', $run.Seen, 1, 30000)
        foreach ($p in $second) { Add-Started $run $p.Id $p.StartTime.ToUniversalTime().Ticks }
        $exited = if ($second.Count) { $second[0].WaitForExit(15000) } else { $false }
        $e = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
        Locked '12 after' $target.Hwnd
        Add-Result $run '12 ... a second launch then shows it: docked, in front, topmost; the launch exits 0' ($exited -and $second[0].ExitCode -eq 0 -and (Is-Shown-Docked $e) -and $e.IsForeground -and (Topmost $noto.Hwnd)) "$(Show-State $e)"

        # 17 - pin by keyboard; never stored
        Set-Pin 'Off'
        Bring-NotoForward
        (Find-ById 'PinToggle').SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @() 0x20
        Add-Result $run '17 the pin is reachable by keyboard: focused, Space toggles it on' ((Pin-State) -eq 'On') "pin=$(Pin-State)"
        Restart-Noto @{}
        Add-Result $run '17 ... and is never stored: a relaunch starts unpinned' ((Pin-State) -eq 'Off') "pin=$(Pin-State)"
        Bring-NotoForward
        $null = Set-ForegroundByKeyboard $target
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '17 ... so losing activation hides it again' (-not $e.WsVisible) (Show-State $e)

        # 5 - hide-on-deactivation OFF in the store
        Restart-Noto @{ 'workspace.hide-on-deactivation' = 'False' }
        Bring-NotoForward
        $m = Trace-Mark
        $null = Set-ForegroundByKeyboard $target
        Start-Sleep -Milliseconds 800
        $e = Ev $noto
        Add-Result $run '5  setting OFF: losing activation leaves Noto shown, above the other app' ((Is-Shown-Docked $e) -and (Fg) -eq $target.Hwnd -and (Above-At-Centre $noto)) "$(Show-State $e); trace: $(Trace-Since $m)"

        # 6 - Escape: the four behaviours on the three surfaces
        $expect = @{
            'LeaveFolderOrHide' = @{ Editor = 'NoteList'; NoteList = 'FolderList'; FolderList = 'hidden' }
            'LeaveFolder'       = @{ Editor = 'NoteList'; NoteList = 'FolderList'; FolderList = 'FolderList' }
            'Hide'              = @{ Editor = 'hidden'; NoteList = 'hidden'; FolderList = 'hidden' }
            'None'              = @{ Editor = 'Editor'; NoteList = 'NoteList'; FolderList = 'FolderList' }
        }
        foreach ($mode in 'LeaveFolderOrHide', 'LeaveFolder', 'Hide', 'None') {
            Restart-Noto @{ 'workspace.hide-on-deactivation' = 'True'; 'workspace.escape' = $mode }
            foreach ($surface in 'Editor', 'NoteList', 'FolderList') {
                To-Surface $surface
                Send-ToNoto @() 0x1B
                $e = Ev $noto
                $got = if (-not $e.WsVisible) { 'hidden' } else { Surface }
                Add-Result $run "6  Escape ($mode) on the $surface -> $($expect[$mode][$surface])" ($got -eq $expect[$mode][$surface]) "got $got; $(Show-State $e)"
            }
        }

        # 6b - an inline input cancels itself first: Escape in the new-folder box clears it and does not hide,
        # although Escape at the folder list hides in the default behaviour
        Restart-Noto @{ 'workspace.escape' = 'LeaveFolderOrHide' }
        To-Surface 'FolderList'
        $input = Find-ById 'FolderNameInput'
        $input.SetFocus(); Start-Sleep -Milliseconds 300
        $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('draft folder'); Start-Sleep -Milliseconds 300
        Send-ToNoto @() 0x1B
        $e = Ev $noto
        $left = if ($e.WsVisible) { (Find-ById 'FolderNameInput').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } else { '(Noto hidden)' }
        Add-Result $run '6  Escape in the new-folder box -> the input is cancelled (cleared), Noto stays shown' ($e.WsVisible -and $left -eq '') "input='$left' $(Show-State $e)"

        # 18 - another application launched over a shown, unpinned Noto takes the foreground -> hidden, every
        # round. The race PR #92 found: the deactivation used to be judged by reading the foreground a few
        # milliseconds later, while it still read Noto, and was dropped. Repeated because it was a race.
        Restart-Noto @{ 'workspace.escape' = 'LeaveFolderOrHide' }
        # Settled first: a click on the target right after a launch can lose the foreground to Noto's own startup
        # activation (measured: the click helper then refuses to type, and the run is INVALID).
        Bring-NotoForward
        for ($r = 1; $r -le 5; $r++) {
            $null = Set-ForegroundByKeyboard $target
            # The controls bracket the whole round, aimed at visible background windows. The "after" one runs once
            # the round is decided, so no probe process sits between the hotkey and the launch under test.
            Locked-On "18.$r before" $bystander.Hwnd
            Chord @($target.Hwnd)
            $pre = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
            $preOk = (Is-Shown-Docked $pre) -and $pre.IsForeground -and (Pin-State) -eq 'Off'
            if (-not $preOk) { Add-Invalid $run "18.$r precondition: Noto was not shown, in front and unpinned ($(Show-State $pre) pin=$(Pin-State))"; continue }
            $m = Trace-Mark
            # Launched by the shell as the user would: pwsh with its console. Measured: with the foreground lock
            # enforced, the window that takes the foreground on launch is the new console's host window (Windows
            # Terminal, another process), not the script's own form, which a launch with no console never brings
            # in front at all. That is the route that produced the race: activation to another process's window.
            [NotoVal.ForegroundChain]::Start()
            $app = Start-TargetApp $run -Name "launched-$r"
            Start-Sleep -Milliseconds 1500
            $seen = [NotoVal.ForegroundChain]::Stop()
            $chain = @($seen | ForEach-Object { "$((Get-Process -Id $_.Key -ErrorAction SilentlyContinue).Name)/$($_.Key)/$($_.Value)" })
            # Precondition: after the launch began, a window of another process held the foreground.
            $other = @($seen | Where-Object { $_.Key -ne 0 -and $_.Key -ne $noto.Pid }).Count -gt 0
            if (-not $other) { Add-Invalid $run "18.$r precondition: the launch never moved the foreground to another process ($($chain -join ' > '))"; Stop-Process -Id $app.Pid -Force; continue }
            $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
            Add-Result $run "18 round $r`: another app launched over a shown, unpinned Noto takes the foreground -> hidden" ((-not $e.WsVisible) -and (Trace-Since $m) -match 'request=Deactivated action=Hide') "before: $(Show-State $pre); after: $(Show-State $e); foreground: $($chain -join ' > ') (launched pid $($app.Pid)); trace: $(Trace-Since $m)"
            Never-Hidden-Active "18 round $r"
            Locked-On "18.$r after" $bystander.Hwnd
            Stop-Process -Id $app.Pid -Force; Start-Sleep -Milliseconds 600
        }

        # 19 - Alt+Tab. After every bracketed claim: an injected Alt can unlock the foreground (README, known
        # limitations). Windows decides where Alt+Tab lands; the product contract is the end state. Either Noto was
        # switched away from and is hidden and not active, or Windows activated it and it is shown and in front.
        # Hidden while foreground or holding focus fails, however it came about.
        for ($r = 1; $r -le 5; $r++) {
            Bring-NotoForward
            if ((Pin-State) -ne 'Off') { throw "19.$r precondition: pinned" }
            $m = Trace-Mark
            Send-ToNoto @(0x12) 0x09
            Start-Sleep -Milliseconds 800
            $e = Ev $noto; $tr = Trace-Since $m
            $away = (-not $e.WsVisible) -and -not $e.IsForeground -and -not $e.KeyboardFocus
            $back = (Is-Shown-Docked $e) -and $e.IsForeground
            $outcome = if ($away) { 'switched away: hidden, not active' } elseif ($back) { 'Windows activated Noto: shown, in front' } else { 'INVALID STATE' }
            Add-Result $run "19 round $r`: Alt+Tab from a shown Noto -> $outcome" ($away -or $back) "$(Show-State $e); trace: $tr"
            if ($tr -match 'request=Deactivated action=Hide.*activation=returned') {
                Add-Result $run "19 round $r`: ... activation returned to the hidden Noto -> reconciled (Activated -> Show)" ($tr -match 'request=Activated action=Show' -and $back) "trace: $tr"
            }
        }
        for ($r = 1; $r -le 2; $r++) {
            Bring-NotoForward
            Send-ToNoto @(0x11) 0x57      # Ctrl+W: hidden
            $null = Set-ForegroundByKeyboard $target
            $m = Trace-Mark
            Send-ToTarget @(0x12) 0x09
            Start-Sleep -Milliseconds 800
            $e = Ev $noto
            Add-Result $run "19 hidden round $r`: Alt+Tab from another app while Noto is hidden -> not hidden and active" (-not ((-not $e.WsVisible) -and ($e.IsForeground -or $e.KeyboardFocus))) "$(Show-State $e); trace: $(Trace-Since $m)"
        }

        # 11 - virtual desktops. Last on purpose: switching desktops injects Win+Ctrl chords, and injected
        # modifiers can unlock the foreground for the next SetForegroundWindow (README, known limitations), so no
        # negative-control-bracketed claim may follow them.
        Restart-Noto @{ 'workspace.escape' = 'LeaveFolderOrHide' }

        # 11b - pinned: a desktop switch leaves Noto on its desktop; the hotkey makes Windows switch back
        $homeDesk = (Get-DesktopState).Current; $countBefore = (Get-DesktopState).All.Count
        Set-Pin 'On'
        Bring-NotoForward
        Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.D
        $other = (Get-DesktopState).Current
        Start-Sleep -Milliseconds 800
        $e = Ev $noto
        Add-Result $run '11 pinned + a desktop switch -> Noto stays shown on its own desktop (cloaked here)' ($other -ne $homeDesk -and $e.WsVisible -and $e.Cloaked -ne 0) "$(Show-State $e)"
        if (Test-ChordFree) { throw 'refusing to send the chord on the other desktop: Noto does not hold it' }
        $one = @(@(0x11, 0x12, 0x5B) | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk(0x20, $false), [NotoVal.Win32]::Vk(0x20, $true)) + @(@(0x5B, 0x12, 0x11) | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })
        [NotoVal.Win32]::Keys(@($one)); Start-Sleep -Seconds 2
        $e = Ev $noto; $now11 = (Get-DesktopState).Current
        Add-Result $run '11 ... the hotkey from the other desktop -> Windows switches back; Noto in front' ($now11 -eq $homeDesk -and $e.IsForeground -and (Is-Shown-Docked $e)) "home=$homeDesk other=$other now=$now11 $(Show-State $e)"
        if ((Get-DesktopState).All -contains $other) { Step-ToDesktop $run $other; if ((Get-DesktopState).Current -eq $other) { Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.F4 } }
        Step-ToDesktop $run $homeDesk
        Set-Pin 'Off'
        Add-Result $run '11 ... the pin toggles off again' ((Pin-State) -eq 'Off') ''

        # 11a - unpinned: a desktop switch hides Noto; the hotkey shows it on the new desktop
        Bring-NotoForward
        $m = Trace-Mark
        Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.D
        $other = (Get-DesktopState).Current
        $e = Wait-Presence $noto { param($e) -not $e.WsVisible }
        Add-Result $run '11 unpinned + a desktop switch -> hidden (a deactivation)' ($other -ne $homeDesk -and -not $e.WsVisible) "$(Show-State $e); trace: $(Trace-Since $m)"
        Never-Hidden-Active '11 after the desktop switch'
        [NotoVal.Win32]::Keys(@($one)); Start-Sleep -Seconds 2
        $e = Ev $noto; $now11 = (Get-DesktopState).Current
        Add-Result $run '11 ... the hotkey on the new desktop -> shown there (no switch), docked, in front' ($now11 -eq $other -and (Is-Shown-Docked $e) -and $e.IsForeground) "home=$homeDesk other=$other now=$now11 $(Show-State $e)"
        if ((Get-DesktopState).All -contains $other) { Step-ToDesktop $run $other; if ((Get-DesktopState).Current -eq $other) { Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.F4 } }
        Step-ToDesktop $run $homeDesk
        Add-Result $run '11 cleanup: back on the run desktop, the extra desktops gone' (((Get-DesktopState).Current -eq $homeDesk) -and ((Get-DesktopState).All.Count -eq $countBefore)) ''

        Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $noto) ''
    }
}
finally {
    $log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($log.All())
}
exit $code
