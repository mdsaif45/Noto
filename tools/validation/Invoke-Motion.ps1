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
    4  surfaces: entering a folder slides in going forward, Escape back going back; Escape is pressed only once the
       note list holds keyboard focus (where Escape goes back), otherwise the run is INVALID, not failed
    5  the hide's two saves (animations on): unsaved text is saved before the exit starts, text typed during the exit
       is saved by a second save when it completes, and a Ctrl+W during the exit starts no second hide; one exit, one
       completion, no write after the window is hidden. Off: hidden at once by the one save, no exit, no completion
    6  a failed second save (animations on): the note is in the recycle bin, text typed during the exit cannot be
       saved, so the content comes back, the window stays shown and in front with the editor's notice and the text,
       and the editor still types; with the note restored, the next hide completes (never stuck Hiding) and saves.
       Off: the failed save keeps the window shown at once, with no exit and no completion to wait for
    The database is read in this process, read-only, every few milliseconds through a hide (Noto's database is in WAL
    mode, so the reader never blocks Noto's write).

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
$NoteId = '01J0000000000000000000N0M1'
$Original = 'Motion validation note'

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
function Press([int[]] $Mods, [int] $Key) {
    <# The inputs for one key with its modifiers, to be sent in one batch with others (Batch-Now). #>
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    @(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) }))
}
function Batch-Now([object[]] $Inputs) {
    <# Keys sent as one batch, so the ones after the chord arrive while the content leaves; only while Noto is in front. #>
    if ((Fg) -ne $noto.Hwnd) { throw 'refusing keys: Noto is not the foreground window' }
    [NotoVal.Win32]::Keys(@($Inputs))
}
function Open-NoteReader {
    <# A read-only connection to this run's database, kept open so a hide can be sampled every few milliseconds. #>
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$(Join-Path $data 'noto.db');Mode=ReadOnly"); $c.Open(); $c
}
function Note-Row($Conn) {
    $cmd = $Conn.CreateCommand(); $cmd.CommandText = 'SELECT Content, UpdatedAt FROM Notes WHERE Id = $n'; [void]$cmd.Parameters.AddWithValue('$n', $NoteId)
    $r = $cmd.ExecuteReader(); try { if ($r.Read()) { [pscustomobject]@{ Content = $r.GetString(0); UpdatedAt = $r.GetString(1) } } } finally { $r.Dispose(); $cmd.Dispose() }
}
function Watch-Note($Conn, [int] $Mark, [string] $EndPattern) {
    <# Every distinct (content, updated) state of the note from now until 300 ms after Noto traces $EndPattern (at most 4 s), in order. #>
    $states = [System.Collections.Generic.List[object]]::new(); $sw = [Diagnostics.Stopwatch]::StartNew(); $end = -1
    while ($sw.ElapsedMilliseconds -lt 4000) {
        $row = Note-Row $Conn
        if ($states.Count -eq 0 -or $states[$states.Count - 1].Content -ne $row.Content -or $states[$states.Count - 1].UpdatedAt -ne $row.UpdatedAt) { $states.Add($row) }
        if ($end -lt 0 -and (First $Mark $EndPattern)) { $end = $sw.ElapsedMilliseconds }
        if ($end -ge 0 -and $sw.ElapsedMilliseconds -gt $end + 300) { break }
        Start-Sleep -Milliseconds 5
    }
    , $states.ToArray()
}
function Writes($Before, [object[]] $States) {
    <# The writes after $Before, in order: each new UpdatedAt is one save that reached the database. #>
    $seen = @{}; @($States | Where-Object { $_.UpdatedAt -ne $Before.UpdatedAt -and -not $seen.ContainsKey($_.UpdatedAt) } | ForEach-Object { $seen[$_.UpdatedAt] = 1; $_ })
}
function Bin-Note([bool] $Binned) {
    $at = if ($Binned) { [DateTimeOffset]::UtcNow.ToString('O') } else { [DBNull]::Value }
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = `$t WHERE Id = `$n" -Parameters @{ '$t' = $at; '$n' = $NoteId }
}
function By-Id([string] $Id) { $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function First-Item([string] $ListId) { (By-Id $ListId).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)) }
function Editor-Text { $e = By-Id 'NoteEditor'; if ($e -and -not $e.Current.IsOffscreen) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } }
function Notice { $e = By-Id 'EditorNoticeText'; if ($e -and -not $e.Current.IsOffscreen) { $e.Current.Name } else { '' } }
function Count([int] $Mark, [string] $Pattern) { @(Trace-Lines $Mark | Where-Object { $_ -match $Pattern }).Count }
function Show-Noto([string] $Label) {
    <# From the target app in front, the chord brings Noto forward. #>
    $null = Set-ForegroundByKeyboard $target
    Ctrl "$Label before" $noto.Hwnd
    Chord-Now $target.Hwnd
    if (-not (Until { (Fg) -eq $noto.Hwnd })) { throw "$Label - the chord did not bring Noto forward" }
    Ctrl "$Label after" $target.Hwnd
    Start-Sleep -Milliseconds 400
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
        # One note: case 4 needs a note list that can hold focus, cases 5 and 6 an editor and a row to watch.
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" -Parameters @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = $Original; '$t' = $now }
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
        if ($animations) {
            # Noto hides the window and then traces "done", so the trace is read once that line has arrived, never at
            # the moment the window disappears (measured: read then, the line could still be missing).
            $done = Until { First $m 'done kind=exit step=HideNow hidden=True' } 3
            $hidden = -not (Ev).WsVisible
            $lines = Trace-Lines $m
            $startAt = [array]::IndexOf($lines, (First $m 'start kind=exit ms=167')); $doneAt = [array]::IndexOf($lines, (First $m 'done kind=exit step=HideNow hidden=True'))
            Add-Result $run '2  hide: the content leaves first, the window is hidden only when it has' ($duringVisible -and $done -and $hidden -and $startAt -ge 0 -and $doneAt -gt $startAt) "visible 40 ms in=$duringVisible start@$startAt done@$doneAt; trace: $(Trace-Since $m)"
        }
        else {
            $hidden = Until { -not (Ev).WsVisible } 3
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
        $m = Trace-Mark; Send-ToNoto @($VK.CTRL) 0x28
        # Escape goes back from the note list. Entering a folder by Ctrl+Down leaves keyboard focus on the header (a
        # separate, pre-existing behaviour, the same before Slice 1), where Escape does nothing; so focus is put on the
        # note list's first row first, and the case is INVALID, not failed, if it is not there.
        $noteRow = if (Until { $l = By-Id 'NoteList'; $l -and -not $l.Current.IsOffscreen } 3) { First-Item 'NoteList' }
        if ($noteRow) { $noteRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $noteRow.SetFocus(); Start-Sleep -Milliseconds 300 }
        $focused = $AE::FocusedElement
        if (-not ($noteRow -and [System.Windows.Automation.Automation]::Compare($focused, $noteRow))) {
            Add-Invalid $run "4  the note list does not hold keyboard focus before Escape: focus=$($focused.Current.ControlType.ProgrammaticName)|$($focused.Current.AutomationId)|$($focused.Current.Name)"
        }
        else {
            Send-ToNoto @() 0x1B
            $back = Until { $l = By-Id 'FolderList'; $l -and -not $l.Current.IsOffscreen } 3
            $expected = if ($animations) { 'start kind=surface forward=True[\s\S]*start kind=surface forward=False' } else { '^(?![\s\S]*start kind=surface)' }
            Add-Result $run '4  surfaces: forward into a folder, back out of it' ($back -and (Trace-Since $m) -match $expected) "back=$back; trace: $(Trace-Since $m)"
        }

        # 5 - the hide's two saves
        $row = First-Item 'FolderList'
        $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @($VK.CTRL) 0x28
        $null = Until { $l = By-Id 'NoteList'; $l -and -not $l.Current.IsOffscreen } 3
        $noteRow = First-Item 'NoteList'
        $noteRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $noteRow.SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @() 0x0D
        if (-not (Until { $null -ne (Editor-Text) })) { throw '5 - the note did not open' }
        $text = "unsaved $([guid]::NewGuid().ToString('N').Substring(0, 6))"
        $editor = By-Id 'NoteEditor'
        $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); $editor.SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @($VK.CTRL) 0x23   # Ctrl+End: the caret after the text
        $reader = Open-NoteReader
        try {
            $before = Note-Row $reader   # the editor's text is unsaved: the row still holds the seeded note
            $m = Trace-Mark
            if ($animations -and (Animations-On)) {
                # The chord, then x, y, z and Ctrl+W, in one batch: all of it arrives while the content leaves.
                Batch-Now (@(Press @($VK.CTRL, $VK.ALT, $VK.LWIN) $VK.SPACE) + @(Press @() 0x58) + @(Press @() 0x59) + @(Press @() 0x5A) + @(Press @($VK.CTRL) 0x57))
                $states = Watch-Note $reader $m 'done kind=exit'
                $hidden = -not (Ev).WsVisible
                $writes = Writes $before $states
                $written = @($writes | ForEach-Object { $_.Content })
                $dismissAt = Stamp (First $m 'request=Dismiss action=None'); $doneAt = Stamp (First $m 'done kind=exit')
                Add-Result $run '5  exactly two saves: the unsaved text before the exit, the text typed during it when it completes' ($before.Content -eq $Original -and $writes.Count -eq 2 -and $written[0] -eq $text -and $written[1] -eq "${text}xyz" -and $hidden) "before=[$($before.Content)] writes=$($writes.Count) [$($written -join ' -> ')] hidden=$hidden; trace: $(Trace-Since $m)"
                Add-Result $run '5  input stays live through the exit: the typed text reached the editor and the database' ($written.Count -gt 0 -and $written[-1] -eq "${text}xyz") "last=[$(if ($written.Count) { $written[-1] })]"
                Add-Result $run '5  one exit, one completion; a Ctrl+W during the exit starts no second hide' ((Count $m 'start kind=exit') -eq 1 -and (Count $m 'done kind=exit step=HideNow hidden=True') -eq 1 -and $dismissAt -and $doneAt -and $dismissAt -le $doneAt -and (Count $m 'Noto\.Motion reversed') -eq 0) "dismiss=$dismissAt done=$doneAt; trace: $(Trace-Since $m)"
                $last = Note-Row $reader; Start-Sleep -Milliseconds 1500; $later = Note-Row $reader
                Add-Result $run '5  no write after the window is hidden (no third save)' ($later.Content -eq $last.Content -and $later.UpdatedAt -eq $last.UpdatedAt -and (Count $m 'start kind=exit') -eq 1) "updated=$($last.UpdatedAt) -> $($later.UpdatedAt)"
            }
            else {
                Chord-Now $noto.Hwnd
                $states = Watch-Note $reader $m 'request=Toggle action=Hide'
                $hidden = Until { -not (Ev).WsVisible } 3
                Start-Sleep -Milliseconds 1000
                $writes = Writes $before (@($states) + @(Note-Row $reader))
                Add-Result $run '5  off: hidden at once by exactly one save; no exit, no completion to wait for' ($hidden -and $before.Content -eq $Original -and $writes.Count -eq 1 -and $writes[0].Content -eq $text -and (Count $m 'start kind=exit') -eq 0 -and (Count $m 'done kind=exit') -eq 0) "writes=$($writes.Count); trace: $(Trace-Since $m)"
            }

            # 6 - a failed second save
            Show-Noto '6'
            Bin-Note $true
            try {
                if ($animations -and (Animations-On)) {
                    $shownText = Editor-Text
                    $before = Note-Row $reader
                    $m = Trace-Mark
                    Batch-Now (@(Press @($VK.CTRL, $VK.ALT, $VK.LWIN) $VK.SPACE) + @(Press @() 0x51))   # the chord, then q while the content leaves
                    $states = Watch-Note $reader $m 'done kind=exit'
                    $null = Until { (Trace-Since $m) -match 'done kind=enter' } 3
                    $e = Ev
                    $writes = Writes $before $states
                    Add-Result $run '6  the second save fails: the content comes back, the window stays shown and in front, with the notice and the text' ((First $m 'done kind=exit step=Restore hidden=False') -and $e.WsVisible -and (Fg) -eq $noto.Hwnd -and (Notice) -match 'recycle bin' -and (Editor-Text) -eq "${shownText}q" -and $writes.Count -eq 0 -and $before.Content -eq $shownText) "writes=$($writes.Count) editor=[$(Editor-Text)] notice='$(Notice)' visible=$($e.WsVisible); trace: $(Trace-Since $m)"
                    Send-ToNoto @() 0x57
                    Add-Result $run '6  the editor still types after the restore' ((Editor-Text) -eq "${shownText}qw") "editor=[$(Editor-Text)]"
                    Bin-Note $false
                    $before = Note-Row $reader
                    $m = Trace-Mark
                    Chord-Now $noto.Hwnd
                    $states = Watch-Note $reader $m 'done kind=exit'
                    $writes = Writes $before $states
                    Add-Result $run '6  never stuck Hiding: with the note restored, the next hide completes, with exactly one save of the text' ((First $m 'done kind=exit step=HideNow hidden=True') -and -not (Ev).WsVisible -and $writes.Count -eq 1 -and $writes[0].Content -eq "${shownText}qw" -and (Count $m 'start kind=exit') -eq 1) "writes=$($writes.Count) [$(@($writes | ForEach-Object { $_.Content }) -join ' -> ')]; trace: $(Trace-Since $m)"
                }
                else {
                    $failing = "unsaved $([guid]::NewGuid().ToString('N').Substring(0, 6))"
                    $editor = By-Id 'NoteEditor'
                    $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($failing); $editor.SetFocus(); Start-Sleep -Milliseconds 300
                    $m = Trace-Mark
                    Chord-Now $noto.Hwnd
                    Start-Sleep -Milliseconds 1000
                    Add-Result $run '6  off: a failed save keeps the window shown at once, with no exit and no completion' ((Ev).WsVisible -and (Notice) -match 'recycle bin' -and (Count $m 'start kind=exit') -eq 0 -and (Count $m 'done kind=exit') -eq 0) "notice='$(Notice)'; trace: $(Trace-Since $m)"
                    Bin-Note $false
                    $m = Trace-Mark
                    Chord-Now $noto.Hwnd
                    Add-Result $run '6  off: with the note restored, the next hide is at once and saves' ((Until { -not (Ev).WsVisible } 3) -and (Until { (Note-Row $reader).Content -eq $failing } 3) -and (Count $m 'start kind=exit') -eq 0) "trace: $(Trace-Since $m)"
                }
            }
            finally { Bin-Note $false }
        }
        finally { $reader.Close(); $reader.Dispose(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }

        Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $noto) ''
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
}
exit $code
