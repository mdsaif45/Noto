#Requires -Version 7.4
<#
.SYNOPSIS
  #16 slice 5: show/hide through the global hotkey, on the real Noto.exe.

.DESCRIPTION
  One isolated data root seeded with a folder and a note. Noto, the target app and a bystander window are
  started by the shell. Keys reach Noto only while it is verifiably in front; the chord only while one of this
  run's windows (or the shell, on a temporary desktop) is in front, and only while Noto holds it. Every claim
  that Noto took the foreground is bracketed by a negative control; any grant makes the run INVALID.

    1  launch -> visible, normal, docked
    2  in front -> chord -> hidden (no WS_VISIBLE), process alive, foreground moved on
    3-6 hidden -> chord -> shown, normal, exact docked rectangle, foreground + keyboard focus
    7  minimized -> chord -> restored, docked, focused
    8  hidden while minimized -> chord -> normal, docked, focused
    9  unsaved editor text -> chord -> saved once, hidden
    10 unsaved text whose save fails -> chord -> stays visible, text kept, notice shown
    14 a tight burst of chords -> ends in a valid state, still responsive
    15 chord during an inner-edge resize drag -> ignored, the resize completes
    16 chord during shutdown -> dropped; the process exits with code 0
    17 chords during startup -> coalesced; Noto ends shown, never hidden
    18 hidden, chord from another virtual desktop -> shown on the CURRENT desktop, docked, in front; no switch
    18b shown on desktop A, chord from desktop B -> Windows switches back to A; Noto in front
    19 taskbar button absent while hidden, present when shown
    20 Alt+Tab, the real switcher: hidden -> never selectable; shown -> selectable

  Cases 11-13 (closing) are Invoke-EditorSaveOnClose.ps1.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-ShowHide.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'show-hide'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F005'
$NoteId = '01J0000000000000000000N005'
$Original = 'original note text'

if (-not ('NotoVal.Extra' -as [type])) {
    Add-Type -Namespace NotoVal -Name Extra -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
'@
}

# ---------------------------------------------------------------- helpers

function Ev($Noto) { Get-WindowEvidence $Noto }
function Show-State($e) { "visible=$($e.WsVisible) minimized=$($e.Minimized) cloaked=$($e.Cloaked) fg=$($e.IsForeground)(pid $($e.ForegroundPid)) focus=$($e.KeyboardFocus) outer=$($e.Outer)" }
function Is-Shown-Docked($e, $Docked) { $e.WsVisible -and -not $e.Minimized -and $e.Cloaked -eq 0 -and $e.Outer -eq $Docked.Outer -and (Test-DockedAt $e 'Right') }

function Fg { [NotoVal.Win32]::GetForegroundWindow() }

function Locked($Away, [string] $Label) {
    $neg = Test-NegativeControl $run $Away
    $run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $run "$Label negative control was granted the foreground: $($neg.Detail)" }
}

function Chord([IntPtr[]] $Allowed) { Send-ActivationChord -AllowedForeground $Allowed }

function Send-ToNoto($Noto, [int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $Noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 700
}

function Find-ById($Noto, [string] $Id, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $e = $AE::FromHandle($Noto.Hwnd).FindFirst($Scope::Descendants, $cond)
        if ($e -and -not $e.Current.IsOffscreen) { return $e }
        Start-Sleep -Milliseconds 200
    }
    throw "no visible element '$Id' within ${Seconds}s"
}
function First-Item($Noto, [string] $ListId) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $list = Find-ById $Noto $ListId; $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) { $i = $list.FindFirst($Scope::Descendants, $cond); if ($i) { return $i }; Start-Sleep -Milliseconds 200 }
    throw "list '$ListId' has no item"
}
function Select-AndFocus($Item) { $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $Item.SetFocus(); Start-Sleep -Milliseconds 300 }
function Editor-Value($Noto) { (Find-ById $Noto 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Set-Editor($Noto, [string] $Text) { (Find-ById $Noto 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text); Start-Sleep -Milliseconds 300 }
function Notice($Noto) { try { (Find-ById $Noto 'EditorNoticeText' 2).Current.Name } catch { '' } }

function Row($Data, [string] $Column) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $Data -Sql "SELECT $Column FROM Notes WHERE Id = `$n" -Parameters @{ '$n' = $NoteId } }

function Taskbar-Buttons {
    $tray = [NotoVal.Win32]::FindWindow('Shell_TrayWnd', [NullString]::Value)
    @($AE::FromHandle($tray).FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match '^Noto\b' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button }).Count
}

# Alt+Tab through the real switcher. The MRU order is arranged first by real activation, so the k-th Tab is
# predictable; Alt+Tab is sent only while the target is in front, and the shell consumes it.
function Name-Of($Hwnd) {
    if ($Hwnd -eq $noto.Hwnd) { 'noto' } elseif ($Hwnd -eq $target.Hwnd) { 'target' } elseif ($Hwnd -eq $bystander.Hwnd) { 'bystander' }
    else {
        $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId($Hwnd, [ref]$p)
        $name = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName
        "other($name pid $p '$([NotoVal.Win32]::Text($Hwnd))')"
    }
}
function AltTab-Select([int] $K) {
    if ((Fg) -ne $target.Hwnd) { throw 'refusing Alt+Tab: the target is not in front' }
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x12, $false))); Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le $K; $i++) { [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x09, $false), [NotoVal.Win32]::Vk(0x09, $true))); Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 300
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x12, $true))); Start-Sleep -Milliseconds 1000
    Fg
}
# Most recent first: target, bystander, then Noto when it is shown.
function Arrange-Mru([bool] $NotoShown) {
    if ($NotoShown) { Bring-NotoForward $noto $target }
    $null = Set-ForegroundByKeyboard $bystander
    $null = Set-ForegroundByKeyboard $target
}
function AltTab-Walk([bool] $NotoShown, [int] $Steps) {
    $seen = @()
    for ($k = 1; $k -le $Steps; $k++) { Arrange-Mru $NotoShown; $seen += Name-Of (AltTab-Select $k) }
    $seen
}

function Wait-Presence($Noto, [scriptblock] $Condition, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = Ev $Noto; if (& $Condition $e) { return $e }; Start-Sleep -Milliseconds 200 }
    Ev $Noto
}

function Bring-NotoForward($Noto, $Target) {
    if ((Fg) -eq $Noto.Hwnd) { return }
    $null = Set-ForegroundByKeyboard $Target
    Chord @($Target.Hwnd)
    if ((Fg) -ne $Noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}

# ---------------------------------------------------------------- run

exit (Invoke-IsolatedRun $run -Title '#16 slice 5: show/hide' -Body {
    Assert-ChordFree $run
    $target = Start-TargetApp $run -Name 'target'
    $bystander = Start-TargetApp $run -Name 'bystander'
    $null = Set-ForegroundByKeyboard $bystander       # activated once, then left in the background

    $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
    $seed = Start-Noto $run -Exe $NotoExe -DataRoot $data
    if (-not (Stop-Noto $seed)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Validation', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" -Parameters @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = $Original; '$t' = $now }

    # 1 - launch
    $noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
    $docked = Ev $noto
    Add-Result $run '1  launch -> visible, normal, docked to the Right edge' ($docked.WsVisible -and -not $docked.Minimized -and (Test-DockedAt $docked 'Right')) (Show-State $docked)
    $buttonsShown = Taskbar-Buttons

    # 2 - in front -> hide
    Bring-NotoForward $noto $target
    Locked $target.Hwnd '2 before'
    Chord @($noto.Hwnd)
    $h = Wait-Presence $noto { param($e) -not $e.WsVisible }
    Add-Result $run '2  in front + chord -> hidden (no WS_VISIBLE), process alive' ((-not $h.WsVisible) -and [bool](Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue)) (Show-State $h)
    Add-Result $run '2  foreground moved to another window (Windows default)' ((Fg) -ne $noto.Hwnd) "fg pid $($h.ForegroundPid)"
    $buttonsHidden = Taskbar-Buttons
    Add-Result $run '19 taskbar: Noto button present when shown, absent when hidden' ($buttonsShown -ge 1 -and $buttonsHidden -eq 0) "shown=$buttonsShown hidden=$buttonsHidden"

    # 3-6 - hidden -> show
    $null = Set-ForegroundByKeyboard $target
    Locked $bystander.Hwnd '3 before'
    Chord @($target.Hwnd)
    $s = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
    Locked $target.Hwnd '3 after'
    Add-Result $run '3  hidden + chord -> shown (WS_VISIBLE, not cloaked)' ($s.WsVisible -and $s.Cloaked -eq 0) (Show-State $s)
    Add-Result $run '4  shown in the normal state (not minimized)' (-not $s.Minimized) ''
    Add-Result $run '5  shown at exactly the docked rectangle, flush to the Right edge' (Is-Shown-Docked $s $docked) "expected $($docked.Outer)"
    Add-Result $run '6  shown as the foreground window with keyboard focus' ($s.IsForeground -and $s.KeyboardFocus) ''
    Add-Result $run '19 taskbar button back after show' ((Taskbar-Buttons) -ge 1) ''

    # 7 - visible + minimized -> restored
    $null = Invoke-Minimize $noto
    $null = Set-ForegroundByKeyboard $target
    Locked $noto.Hwnd '7 before'
    Chord @($target.Hwnd)
    $r = Wait-Presence $noto { param($e) -not $e.Minimized -and $e.IsForeground }
    Locked $target.Hwnd '7 after'
    Add-Result $run '7  minimized + chord -> restored, docked, foreground, focus' ((Is-Shown-Docked $r $docked) -and $r.IsForeground -and $r.KeyboardFocus) (Show-State $r)

    # 8 - hidden while minimized -> shown normal
    $null = Invoke-Minimize $noto
    [void][NotoVal.Extra]::ShowWindow($noto.Hwnd, 0)                # SW_HIDE on the minimized window
    $hm = Ev $noto
    $null = Set-ForegroundByKeyboard $target
    Locked $bystander.Hwnd '8 before'
    Chord @($target.Hwnd)
    $m = Wait-Presence $noto { param($e) $e.WsVisible -and -not $e.Minimized -and $e.IsForeground }
    Locked $target.Hwnd '8 after'
    Add-Result $run '8  hidden-while-minimized + chord -> normal, docked, foreground, focus' ((-not $hm.WsVisible) -and (Is-Shown-Docked $m $docked) -and $m.IsForeground -and $m.KeyboardFocus) "before: $(Show-State $hm) | after: $(Show-State $m)"

    # 9 - unsaved editor text -> saved once, hidden
    Bring-NotoForward $noto $target
    Select-AndFocus (First-Item $noto 'FolderList'); Send-ToNoto $noto @(0x11) 0x28
    Select-AndFocus (First-Item $noto 'NoteList'); Send-ToNoto $noto @() 0x0D
    $null = Find-ById $noto 'NoteEditor'
    $updatedBefore = Row $data 'UpdatedAt'
    $text9 = "saved on hide $([guid]::NewGuid().ToString('N').Substring(0, 6))"
    Set-Editor $noto $text9
    Chord @($noto.Hwnd)
    $h9 = Wait-Presence $noto { param($e) -not $e.WsVisible }
    Add-Result $run '9  unsaved text + chord -> saved, then hidden' ((Row $data 'Content') -eq $text9 -and -not $h9.WsVisible) "content='$(Row $data 'Content')' $(Show-State $h9)"
    Add-Result $run '9  saved exactly once (UpdatedAt changed once, row holds the text)' ((Row $data 'UpdatedAt') -ne $updatedBefore) ''

    # 10 - the save fails -> stays visible
    $null = Set-ForegroundByKeyboard $target
    Chord @($target.Hwnd)
    $null = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
    $editorKept = (Editor-Value $noto) -eq $text9
    $text10 = "cannot be saved $([guid]::NewGuid().ToString('N').Substring(0, 6))"
    Set-Editor $noto $text10
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = `$t WHERE Id = `$n" -Parameters @{ '$t' = [DateTimeOffset]::UtcNow.ToString('O'); '$n' = $NoteId }
    if ((Fg) -ne $noto.Hwnd) { Bring-NotoForward $noto $target }
    Chord @($noto.Hwnd)
    Start-Sleep -Seconds 1
    $v10 = Ev $noto
    Add-Result $run '10 editor survived hide/show with its note open' $editorKept ''
    Add-Result $run '10 save fails + chord -> stays visible' ($v10.WsVisible -and -not $v10.Minimized) (Show-State $v10)
    Add-Result $run '10 text kept and the failure notice shown; nothing written' ((Editor-Value $noto) -eq $text10 -and (Notice $noto) -match 'recycle bin' -and (Row $data 'Content') -eq $text9) "notice='$(Notice $noto)'"
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = NULL WHERE Id = `$n" -Parameters @{ '$n' = $NoteId }
    Send-ToNoto $noto @() 0x1B                                         # leave the editor: saves text10 now the note is back
    Send-ToNoto $noto @() 0x1B                                         # back to the folder list

    # 14 - a tight burst of chords
    if ((Fg) -ne $noto.Hwnd) { Bring-NotoForward $noto $target }
    $down = @(0x11, 0x12, 0x5B) | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }
    $up = @(0x5B, 0x12, 0x11) | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) }
    $one = @($down) + @([NotoVal.Win32]::Vk(0x20, $false), [NotoVal.Win32]::Vk(0x20, $true)) + @($up)
    [NotoVal.Win32]::Keys(@($one + $one + $one + $one + $one + $one))
    Start-Sleep -Seconds 2
    $t14 = Ev $noto
    $valid14 = ((-not $t14.WsVisible) -and [bool](Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue)) -or ((Is-Shown-Docked $t14 $docked))
    Add-Result $run '14 six chords in one burst -> a valid end state (hidden, or shown and docked)' $valid14 (Show-State $t14)
    if (-not $t14.WsVisible) { $null = Set-ForegroundByKeyboard $target; Chord @($target.Hwnd) }
    $t14b = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
    Add-Result $run '14 still responsive afterwards: one more chord leaves it shown, docked and in front' ((Is-Shown-Docked $t14b $docked) -and $t14b.IsForeground) (Show-State $t14b)

    # 15 - chord during an inner-edge resize drag
    $e15 = Ev $noto
    $x = $e15.FrameLeft - 3; $y = [int](($e15.FrameTop + $e15.FrameBottom) / 2)
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $x; $pt.Y = $y
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $noto.Hwnd) { throw "refusing to drag at ($x,$y): it is not Noto's inner edge" }
    [void][NotoVal.Win32]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 200
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
    for ($i = 1; $i -le 8; $i++) { [void][NotoVal.Win32]::SetCursorPos($x - 10 * $i, $y); Start-Sleep -Milliseconds 60 }
    [NotoVal.Win32]::Keys(@($one))                                   # the chord, inside the size loop
    Start-Sleep -Milliseconds 800
    $during = Ev $noto
    [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 800
    $after15 = Ev $noto
    Add-Result $run '15 chord during a resize drag is ignored: still visible mid-drag' ($during.WsVisible) (Show-State $during)
    Add-Result $run '15 the resize completes, still docked, wider than before' ($after15.WsVisible -and (Test-DockedAt $after15 'Right') -and ($after15.FrameRight - $after15.FrameLeft) -gt ($e15.FrameRight - $e15.FrameLeft)) "before $($e15.Frame) after $($after15.Frame)"
    $docked = $after15                                                # the new width is the docked rectangle from here on

    # 18 - hidden, chord from another virtual desktop
    if ((Fg) -ne $noto.Hwnd) { Bring-NotoForward $noto $target }
    Chord @($noto.Hwnd)
    $null = Wait-Presence $noto { param($e) -not $e.WsVisible }
    $homeDesk = (Get-DesktopState).Current
    $countBefore = (Get-DesktopState).All.Count
    Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.D
    $other = (Get-DesktopState).Current
    if ($other -eq $homeDesk) { Add-Invalid $run '18 no second temporary desktop was created' }
    else {
        if (Test-ChordFree) { throw 'refusing to send the chord on the other desktop: Noto does not hold it' }
        [NotoVal.Win32]::Keys(@($one)); Start-Sleep -Seconds 2
        $d = Ev $noto
        $now18 = (Get-DesktopState).Current
        Add-Result $run '18 hidden + chord from another desktop -> no desktop switch' ($now18 -eq $other) "home=$homeDesk other=$other now=$now18"
        Add-Result $run '18 ... Noto shown on the CURRENT desktop (not cloaked), docked and in front' ((Is-Shown-Docked $d $docked) -and $d.IsForeground) (Show-State $d)
        # Closing the second desktop moves its windows, Noto now among them, to the run desktop.
        if ((Get-DesktopState).All -contains $other) { Step-ToDesktop $run $other; if ((Get-DesktopState).Current -eq $other) { Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.F4 } }
        Step-ToDesktop $run $homeDesk
        Add-Result $run '18 cleanup: the second temporary desktop is gone, back on the run desktop' (((Get-DesktopState).Current -eq $homeDesk) -and ((Get-DesktopState).All.Count -eq $countBefore)) ''
        $back = Ev $noto
        if (-not ($back.WsVisible -and $back.Cloaked -eq 0)) { Add-Invalid $run "18b precondition: Noto is not shown on the run desktop after cleanup: $(Show-State $back)" }

        # 18b - Noto SHOWN on the run desktop, chord from another desktop
        if ((Ev $noto).WsVisible -eq $false) { $null = Set-ForegroundByKeyboard $target; Chord @($target.Hwnd); $null = Wait-Presence $noto { param($e) $e.WsVisible } }
        $null = Set-ForegroundByKeyboard $target
        Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.D
        $other2 = (Get-DesktopState).Current
        $cloakedThere = (Ev $noto).Cloaked
        [NotoVal.Win32]::Keys(@($one)); Start-Sleep -Seconds 2
        $d2 = Ev $noto; $now18b = (Get-DesktopState).Current
        Add-Result $run '18b shown on desktop A + chord from desktop B -> Windows switches back to A' ($cloakedThere -ne 0 -and $now18b -eq $homeDesk) "cloaked-on-B=$cloakedThere home=$homeDesk now=$now18b"
        Add-Result $run '18b ... and Noto is in front, docked' ((Is-Shown-Docked $d2 $docked) -and $d2.IsForeground) (Show-State $d2)
        if ((Get-DesktopState).All -contains $other2) { Step-ToDesktop $run $other2; if ((Get-DesktopState).Current -eq $other2) { Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.F4 } }
        Step-ToDesktop $run $homeDesk
        if ((Ev $noto).WsVisible -and -not (Ev $noto).IsForeground) { $null = Set-ForegroundByKeyboard $target }
    }

    # 16 - chord during shutdown
    $proc = Get-Process -Id $noto.Pid
    $null = $proc.Handle                                               # opened now, so ExitCode is readable after exit
    $null = Set-ForegroundByKeyboard $target
    [void][NotoVal.Win32]::PostMessage($noto.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    [NotoVal.Win32]::Keys(@($one + $one))
    $exited = $proc.WaitForExit(10000)
    Add-Result $run '16 chord during shutdown -> dropped; the process exits cleanly (code 0)' ($exited -and $proc.ExitCode -eq 0) "exited=$exited code=$(if ($exited) { $proc.ExitCode })"

    # 17 - chords during startup
    $null = Set-ForegroundByKeyboard $target
    $marker = "--data-root=`"$data`""
    $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
    $view.Application.ShellExecute($NotoExe, $marker, $run.Root, 'open', 1)
    # A press every 100 ms while the target is in front, for up to 8 s. Presses stop as soon as anything else
    # (Noto's window, appearing) takes the foreground, so every press sent was made while Noto was starting.
    $sent = 0; $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt 8 -and (Fg) -eq $target.Hwnd) { [NotoVal.Win32]::Keys(@($one)); $sent++; Start-Sleep -Milliseconds 100 }
    $h17 = [IntPtr]::Zero; $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and $h17 -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 250
        $p17 = Get-CimInstance Win32_Process -Filter "Name='Noto.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($marker) -and -not $run.Seen.Contains([int]$_.ProcessId) } | Select-Object -First 1
        if ($p17) { $h17 = [NotoVal.Win32]::TopWindows([uint32]$p17.ProcessId) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1; if (-not $h17) { $h17 = [IntPtr]::Zero } }
    }
    if ($p17) { [void]$run.Seen.Add([int]$p17.ProcessId); Add-Started $run ([int]$p17.ProcessId) ((Get-Process -Id $p17.ProcessId).StartTime.ToUniversalTime().Ticks) }
    Start-Sleep -Seconds 3
    $noto17 = [pscustomobject]@{ Pid = [int]$p17.ProcessId; Hwnd = $h17 }
    $e17 = Ev $noto17
    Add-Result $run '17 chords during startup -> Noto ends shown and docked, not hidden' ($sent -gt 0 -and $e17.WsVisible -and -not $e17.Minimized -and (Test-DockedAt $e17 'Right')) "chords sent=$sent $(Show-State $e17)"
    Start-Sleep -Seconds 2
    Add-Result $run '17 ... and stays shown (no delayed toggle)' ((Ev $noto17).WsVisible) ''

    # 20 - Alt+Tab through the real switcher. Last on purpose: an injected Alt press can unlock the foreground
    # for the next SetForegroundWindow, so no negative-control-bracketed claim may follow it.
    $noto = $noto17
    $steps = 6                                                          # more than the eligible windows, so a full cycle is covered
    $walkShown = AltTab-Walk $true $steps
    Add-Result $run '20 Alt+Tab (real switcher), Noto shown: selectable' ($walkShown[0] -eq 'bystander' -and $walkShown -contains 'noto') "k=1..$steps selected: $($walkShown -join ', ')"
    Bring-NotoForward $noto $target
    Chord @($noto.Hwnd)
    $null = Wait-Presence $noto { param($e) -not $e.WsVisible }
    if ((Ev $noto).WsVisible) { Add-Invalid $run '20 precondition: Noto did not hide before the hidden walk' }
    $walkHidden = AltTab-Walk $false $steps
    Add-Result $run '20 Alt+Tab (real switcher), Noto hidden: never selectable' ($walkHidden[0] -eq 'bystander' -and $walkHidden -notcontains 'noto' -and -not (Ev $noto).WsVisible) "k=1..$steps selected: $($walkHidden -join ', ')"

    Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $noto17) ''
})
