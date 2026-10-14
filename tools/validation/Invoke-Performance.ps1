#Requires -Version 7.4
<#
.SYNOPSIS
  Noto's performance baseline (#11): measured on the real application, repeatable, recorded, so a regression shows.

.DESCRIPTION
  One isolated data root, seeded at a realistic size: NoteCount notes (default 1000) across 20 folders, with the
  measured folder holding a short note, a large note (a deterministic ~100 KB Markdown-style document) and 48 more.
  Noto is started by the shell, as a user starts it, on a temporary virtual desktop. Every timing is taken from
  outside the process: the kernel's process creation time, a window becoming visible, an element appearing in UI
  Automation, a row changing in the database, or a line Noto traces (read from the debug-output channel and stamped
  on arrival). Polling resolves to about 10-30 ms, plus UI Automation's own cost; the numbers compare between runs on
  one machine, not across machines. Medians are true medians (the mean of the middle two for an even count).

  Three startup moments are kept apart, because they are different claims:
    hotkey registered   Noto traces "Noto.Hotkey registered" once RegisterHotKey has succeeded.
    tray added          Noto traces "Noto.Tray added".
    first chord served  The chord, pressed the moment the hotkey is registered, has shown the window: the hotkey
                        actually responding, which also needs Noto's UI thread to be pumping messages.

    cold       The run's first start, from a copy of the build made with unbuffered I/O (robocopy /J) into a new
               folder, so none of Noto's own files are in the file cache: process creation -> hotkey registered,
               tray added, the window visible. Windows' shared components are as warm as after any sign-in, and an
               on-access antivirus scans the new files first, so this is a first start after an install or update:
               an upper bound on a cold start after sign-in. On a new data root, so it also creates the database.
    start      N warm starts on the seeded data root: process creation -> the window visible -> the folder list
               populated; and -> hotkey registered, tray added.
    login      N hidden --login starts: process creation -> hotkey registered, tray added; never a window. In all
               but the last, the chord is pressed as soon as the hotkey is registered: process creation -> the
               window shown and in front (first chord served).
    hotkey     From the last, never-shown --login start: the activation chord -> the window visible and in front.
    idle       Hidden, then shown: CPU over IdleSeconds (ms of CPU, and % of one core), working set and private bytes.
    open/load  A note opened from the list: Enter -> the editor present -> the editor holding the note's text. Short
               and large notes.
    save       Text changed, then the workspace hidden with the hotkey (which saves first). Timed by Noto's own
               "Noto.Save completed" trace, written once the save has returned from the database:
                 completed  the chord -> that trace (end to end: the hotkey, the hide request, the save);
                 duration   the save alone, measured inside Noto and carried on the trace (ms=);
                 observed   the chord -> the row in the database holding the new text, seen by polling it from
                            here. A correctness check, and the measure before the trace existed: each poll is a
                            query from this process, so it resolves only to the poll's own cost.
               -SaveRuns sets how many saves of each note are measured (default: -Runs). The row in
               the database holding the new text. Short and large notes.

  Results are printed and written to <run>\performance.json. No developer-only tool is needed: PowerShell 7 and the
  built Noto.exe.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Performance.ps1 -NotoExe <path>\Noto.exe [-Runs 5] [-IdleSeconds 60] [-NoteCount 1000]
#>
param([Parameter(Mandatory)] [string] $NotoExe, [ValidateRange(2, 50)] [int] $Runs = 5, [ValidateRange(10, 3600)] [int] $IdleSeconds = 60, [ValidateRange(50, 100000)] [int] $NoteCount = 1000, [ValidateRange(0, 200)] [int] $SaveRuns = 0)
if ($SaveRuns -eq 0) { $SaveRuns = $Runs }
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'performance'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F011'
$SmallId = '01J0000000000000000000N011'
$LargeId = '01J0000000000000000000N012'
$Small = 'A short note: a phone number, a reminder, a line of text.'

# A deterministic ~100 KB document shaped like real notes: headings, paragraphs, lists, tasks and code.
$Large = & {
    $sb = [System.Text.StringBuilder]::new()
    for ($s = 1; $sb.Length -lt 100000; $s++) {
        [void]$sb.AppendLine("## Section $s - meeting notes and follow-ups").AppendLine()
        [void]$sb.AppendLine("Discussed the rollout plan for milestone $s with the team. The main risks are the migration window, the support load in week two, and the dependency on the vendor's API version $s.4. Agreed to revisit after the pilot.").AppendLine()
        foreach ($i in 1..6) { [void]$sb.AppendLine("- [$(if ($i % 3 -eq 0) { 'x' } else { ' ' })] Item $s.$i - follow up with owner $i about the budget line and the timeline") }
        [void]$sb.AppendLine().AppendLine('```').AppendLine("select id, title, updated_at from notes where folder_id = $s order by sort_order;").AppendLine('```').AppendLine()
    }
    $sb.ToString()
}

$results = [ordered]@{ exe = $NotoExe; version = (Get-Item $NotoExe).VersionInfo.ProductVersion; machine = $env:COMPUTERNAME; logicalProcessors = [Environment]::ProcessorCount; runs = $Runs; idleSeconds = $IdleSeconds; noteCount = $NoteCount; folderCount = 20; largeNoteChars = $Large.Length }

# ---------------------------------------------------------------- helpers

function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Ms([datetime] $From) { [math]::Round(((Get-Date) - $From).TotalMilliseconds) }
function Until([scriptblock] $Condition, [int] $TimeoutMs = 30000, [int] $PollMs = 10) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds $PollMs }
    $false
}
function Window([int] $ProcessId) {
    [NotoVal.Win32]::TopWindows([uint32]$ProcessId) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' -and [NotoVal.Win32]::IsWindowVisible($_) } | Select-Object -First 1
}
function Element([IntPtr] $Hwnd, [string] $Id) {
    try { $AE::FromHandle($Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) } catch { $null }
}
function FirstItem([IntPtr] $Hwnd, [string] $ListId) {
    $list = Element $Hwnd $ListId
    if (-not $list) { return $null }
    $list.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))
}
function Traced([int] $ProcessId, [string] $Pattern) {
    # The debug-output reader stamps each line as it arrives: "HH:mm:ss.fff pid=… …".
    $line = @($log.Lines($ProcessId)) | Where-Object { $_ -match $Pattern } | Select-Object -First 1
    if ($line -and $line -match '^(\d\d:\d\d:\d\d\.\d\d\d) ') { [datetime]::ParseExact((Get-Date -Format 'yyyy-MM-dd') + ' ' + $Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null) }
}
function Created([int] $ProcessId) { (Get-Process -Id $ProcessId).StartTime }
function Stats([double[]] $Values) {
    $sorted = @($Values | Sort-Object); $n = $sorted.Count; $mid = [int][math]::Floor(($n - 1) / 2)
    $median = if ($n % 2) { $sorted[$mid] } else { ($sorted[$mid] + $sorted[$mid + 1]) / 2 }
    [ordered]@{ n = $n; min = $sorted[0]; median = $median; max = $sorted[-1]; values = $sorted }
}
if ((Stats @(4, 1)).median -ne 2.5 -or (Stats @(3, 1, 2)).median -ne 2 -or (Stats @(5)).median -ne 5) { throw 'Stats: the median is wrong' }
function Since([pscustomobject] $Process, [string] $Pattern, [int] $TimeoutMs = 30000) {
    # Milliseconds from the process's creation to the first line it traces that matches; $null if none in time.
    if (-not (Until { Traced $Process.Pid $Pattern } $TimeoutMs)) { return $null }
    [math]::Round(((Traced $Process.Pid $Pattern) - $Process.Created).TotalMilliseconds)
}
function Keys-ToNoto([int] $Key, [switch] $Ctrl) {
    if ((Fg) -ne $noto.Hwnd) { throw 'refusing keys: Noto is not the foreground window' }
    $press = @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true))
    if ($Ctrl) { $press = @([NotoVal.Win32]::Vk(0x11, $false)) + $press + @([NotoVal.Win32]::Vk(0x11, $true)) }
    [NotoVal.Win32]::Keys($press)
}
function Shown-InFront {
    Set-ForegroundByKeyboard $target | Out-Null
    Send-ActivationChord -AllowedForeground $target.Hwnd
    if (-not (Until { (Fg) -eq $noto.Hwnd } 10000)) { throw 'the hotkey did not bring Noto forward' }
}
function Start-Owner([string] $Switches = '', [string] $Exe = $NotoExe) {
    $p = Start-ViaShell $run -File $Exe -Arguments "--data-root=`"$data`" $Switches".Trim() -Marker "--data-root=`"$data`" $Switches".Trim()
    [pscustomobject]@{ Pid = $p.Pid; Created = Created $p.Pid; Hwnd = [IntPtr]::Zero }
}
function Stop-Owner {
    if ($script:noto -and (Get-Process -Id $script:noto.Pid -ErrorAction SilentlyContinue)) {
        $h = [NotoVal.Win32]::TopWindows([uint32]$script:noto.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1
        if (-not (Stop-Noto ([pscustomobject]@{ Pid = $script:noto.Pid; Hwnd = $h }))) { throw 'Noto did not exit cleanly' }
    }
    $script:noto = $null
}
function Open-Note([string] $NoteId, [string] $Content) {
    # The folder, then the note, selected through UI Automation; Enter opens it, as a user opens it. A hidden and
    # reshown workspace keeps its editor open, so each open starts from the list: Escape leaves the editor first.
    Shown-InFront
    if (Element $noto.Hwnd 'NoteEditor') {
        Keys-ToNoto 0x1B
        $null = Until { -not (Element $noto.Hwnd 'NoteEditor') -or -not [NotoVal.Win32]::IsWindowVisible($noto.Hwnd) } 10000
        if (-not [NotoVal.Win32]::IsWindowVisible($noto.Hwnd)) { Shown-InFront }
        if (Element $noto.Hwnd 'NoteEditor') { throw 'Escape did not leave the editor' }
    }
    # Escape from the editor returns to the folder's note list; otherwise the folder is entered with Ctrl+Down (parity
    # C4; Enter is not bound on the folder list). Which surface is up is decided by which list exists, never by its
    # rows: a collapsed surface is absent from UI Automation, but leaving the editor collapses it a moment before the
    # note list appears, and then re-queries the notes, clearing the rows while it does.
    $script:surface = $null
    $settled = Until { $script:surface = if (Element $noto.Hwnd 'NoteList') { 'notes' } elseif (Element $noto.Hwnd 'FolderList') { 'folders' }; $script:surface } 10000
    if (-not $settled) { throw "no list surface is up (window visible=$([NotoVal.Win32]::IsWindowVisible($noto.Hwnd)) editor=$([bool](Element $noto.Hwnd 'NoteEditor')))" }
    if ($script:surface -eq 'folders') {
        if (-not (Until { ($script:folder = FirstItem $noto.Hwnd 'FolderList') } 10000)) {
            $ids = try { @($AE::FromHandle($noto.Hwnd).FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ } | Select-Object -Unique) -join ',' } catch { "uia error: $($_.Exception.Message)" }
            throw "no folder to select (visible=$([NotoVal.Win32]::IsWindowVisible($noto.Hwnd)) fg=$((Fg) -eq $noto.Hwnd) noteListRows=$([bool](FirstItem $noto.Hwnd 'NoteList')) ids=$ids)"
        }
        $script:folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $script:folder.SetFocus(); Start-Sleep -Milliseconds 200
        Keys-ToNoto 0x28 -Ctrl
    }
    $rowCondition = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $script:rows = @()
    $entered = Until { $l = Element $noto.Hwnd 'NoteList'; if ($l) { $script:rows = @($l.FindAll($Scope::Descendants, $rowCondition)) }; $script:rows.Count -ge 2 } 10000
    if (-not $entered) { throw "Ctrl+Down did not open the folder (notes listed: $($script:rows.Count))" }
    # Leaving the editor re-queries the list and then focuses the row of the note that was open, so a row is chosen
    # only once that has settled, re-read each time, and Enter is pressed only while the chosen row holds focus.
    Start-Sleep -Milliseconds 500
    $index = if ($NoteId -eq $LargeId) { 1 } else { 0 }   # sort order: the short note first
    $focused = Until {
        try {
            $row = @((Element $noto.Hwnd 'NoteList').FindAll($Scope::Descendants, $rowCondition))[$index]
            $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus()
            Start-Sleep -Milliseconds 150
            $row.Current.HasKeyboardFocus -and (Fg) -eq $noto.Hwnd
        }
        catch { $false }
    } 10000 100
    if (-not $focused) { throw 'the note''s row did not keep keyboard focus' }
    $t0 = Get-Date
    Keys-ToNoto 0x0D
    $present = Until { Element $noto.Hwnd 'NoteEditor' } 30000 5
    $editorMs = Ms $t0
    $loaded = Until { $e = Element $noto.Hwnd 'NoteEditor'; $e -and $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value.Length -eq $Content.Replace("`r`n", "`r").Replace("`n", "`r").Length } 60000 5
    $loadMs = Ms $t0
    if (-not ($present -and $loaded)) { throw "the note did not open (editor=$present loaded=$loaded)" }
    [pscustomobject]@{ Editor = $editorMs; Loaded = $loadMs }
}
function Save-ByHide([string] $NoteId, [string] $Text, [string] $Suffix) {
    # The editor normalizes line endings, so the saved row is recognised by the unique text appended to it.
    (Element $noto.Hwnd 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text + $Suffix)
    Start-Sleep -Milliseconds 300
    if ((Fg) -ne $noto.Hwnd) { throw 'Noto is not in front to hide' }
    $seen = @(SaveTraces).Count
    $t0 = Get-Date
    Send-ActivationChord -AllowedForeground $noto.Hwnd -NoWait

    # Timed by Noto's own signal: the trace written once the save has returned from the database.
    $script:saveLine = $null
    $traced = Until { $lines = @(SaveTraces); if ($lines.Count -gt $seen) { $script:saveLine = $lines[$seen]; $true } } 60000 2
    if (-not $traced) { throw 'the hide completed no save' }
    if ($script:saveLine -notmatch '^(\d\d:\d\d:\d\d\.\d\d\d) .*Noto\.Save completed ms=([0-9.]+)') { throw "unreadable save trace: $($script:saveLine)" }
    $completed = [datetime]::ParseExact((Get-Date -Format 'yyyy-MM-dd') + ' ' + $Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null)
    $duration = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)

    # The database probe stays as the correctness check: the row must hold the new text.
    $persisted = Until { (Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql 'SELECT substr(Content, -length($s)) = $s FROM Notes WHERE Id = $n' -Parameters @{ '$n' = $NoteId; '$s' = $Suffix }) -eq 1 } 60000 5
    $observed = Ms $t0
    if (-not $persisted) { throw 'Noto traced a completed save, but the row does not hold the new text' }
    [pscustomobject]@{ Completed = [math]::Round(($completed - $t0).TotalMilliseconds); Duration = $duration; Observed = $observed }
}
function SaveTraces { @($log.Lines($noto.Pid)) | Where-Object { $_ -match 'Noto\.Save completed' } }
function Idle([string] $Label) {
    $p = Get-Process -Id $noto.Pid
    Start-Sleep -Seconds 5   # let start-up work settle before the window opens
    $p.Refresh(); $cpu0 = $p.TotalProcessorTime.TotalMilliseconds; $t0 = Get-Date
    Start-Sleep -Seconds $IdleSeconds
    $p.Refresh(); $cpuMs = $p.TotalProcessorTime.TotalMilliseconds - $cpu0; $wall = ((Get-Date) - $t0).TotalMilliseconds
    [ordered]@{ state = $Label; cpuMs = [math]::Round($cpuMs); cpuPercentOfOneCore = [math]::Round(100 * $cpuMs / $wall, 3); workingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); privateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1) }
}

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title '#11 performance baseline' -Body {
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $script:data = Join-Path $run.Root 'root'; New-Item -ItemType Directory $data | Out-Null
        $script:target = Start-TargetApp $run

        # cold: the build copied with unbuffered I/O, so none of Noto's files are in the file cache; a new data root
        $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
        $coldDir = Join-Path $run.Root 'cold-build'
        $null = robocopy (Split-Path $NotoExe) $coldDir /E /J /R:0 /W:0 /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw "the unbuffered copy failed (robocopy $LASTEXITCODE)" }
        foreach ($f in 'Noto.exe', 'Noto.dll') {
            if ((Get-FileHash (Join-Path (Split-Path $NotoExe) $f)).Hash -ne (Get-FileHash (Join-Path $coldDir $f)).Hash) { throw "the cold copy of $f differs" }
        }
        Set-ForegroundByKeyboard $target | Out-Null
        $script:noto = Start-Owner -Exe (Join-Path $coldDir 'Noto.exe')
        $coldVisible = Until { Window $noto.Pid } 60000 5; $coldWindowMs = Ms $noto.Created
        $coldHotkey = Since $noto 'Noto\.Hotkey registered' 60000
        $coldTray = Since $noto 'Noto\.Tray added' 60000
        if (-not ($coldVisible -and $null -ne $coldHotkey -and $null -ne $coldTray)) { throw "the cold start was not measured (visible=$coldVisible hotkey=$coldHotkey tray=$coldTray)" }
        $results.cold = [ordered]@{ method = 'unbuffered copy (robocopy /J), new data root'; hotkeyRegisteredMs = $coldHotkey; trayAddedMs = $coldTray; windowMs = $coldWindowMs; bootTime = $boot.ToString('O'); minutesSinceBoot = [math]::Round(($noto.Created - $boot).TotalMinutes, 1) }
        Stop-Owner
        if ((Split-Path $coldDir -Parent) -ne $run.Root -or (Split-Path $coldDir -Leaf) -ne 'cold-build') { throw 'refusing to remove an unexpected folder' }
        Remove-Item -LiteralPath $coldDir -Recurse -Force
        Add-Result $run 'cold: uncached build, new data root' $true ("hotkey registered={0} tray added={1} window={2} ms" -f $coldHotkey, $coldTray, $coldWindowMs)
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Performance', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
        foreach ($n in @(@($SmallId, $Small, 1), @($LargeId, $Large, 2))) {
            $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$o, `$t, `$t)" -Parameters @{ '$n' = $n[0]; '$f' = $FolderId; '$c' = $n[1]; '$o' = $n[2]; '$t' = $now }
        }
        # The rest of a realistic store, in one statement: 19 more folders, 48 more notes after the two measured ones in
        # their folder, and the remainder spread over the other folders; each a few hundred characters.
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "WITH RECURSIVE f(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM f WHERE i < 19) INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) SELECT printf('01J00000000000000000000%03d', i), 'Folder ' || i, i + 1, `$t, `$t FROM f" -Parameters @{ '$t' = $now }
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < `$count) INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) SELECT printf('01J0000000000000%010d', i), CASE WHEN i <= 48 THEN `$f ELSE printf('01J00000000000000000000%03d', 1 + (i % 19)) END, 'Note ' || i || char(10) || char(10) || 'Follow up on item ' || i || ' with the team before the review. Check the numbers, the owner and the date, and add the result to the weekly summary.' || char(10) || '- [ ] first step' || char(10) || '- [ ] second step' || char(10), i + 2, `$t, `$t FROM n" -Parameters @{ '$count' = $NoteCount - 2; '$f' = $FolderId; '$t' = $now }
        if ((Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql 'SELECT count(*) FROM Notes') -ne $NoteCount) { throw 'the seeded note count is wrong' }
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'workspace.hide-on-deactivation' -Value 'False'

        # start: window visible, folder list populated; hotkey registered, tray added
        $starts = @()
        foreach ($i in 1..$Runs) {
            Set-ForegroundByKeyboard $target | Out-Null
            $script:noto = Start-Owner
            $visible = Until { Window $noto.Pid } 30000 5; $windowMs = Ms $noto.Created
            $script:noto.Hwnd = Window $noto.Pid
            $usable = Until { FirstItem $noto.Hwnd 'FolderList' } 30000 5; $usableMs = Ms $noto.Created
            $hotkeyMs = Since $noto 'Noto\.Hotkey registered'; $trayMs = Since $noto 'Noto\.Tray added'
            if (-not ($visible -and $usable -and $null -ne $hotkeyMs -and $null -ne $trayMs)) { throw "start $i was not measured (visible=$visible usable=$usable hotkey=$hotkeyMs tray=$trayMs)" }
            $starts += [pscustomobject]@{ Window = $windowMs; Usable = $usableMs; Hotkey = $hotkeyMs; Tray = $trayMs }
            Stop-Owner
            Start-Sleep -Seconds 2
        }
        $results.start = [ordered]@{ windowMs = Stats ($starts.Window); folderListMs = Stats ($starts.Usable); hotkeyRegisteredMs = Stats ($starts.Hotkey); trayAddedMs = Stats ($starts.Tray) }
        Add-Result $run 'start: window, folder list, hotkey registered, tray added' $true ("median window={0} folder list={1} hotkey registered={2} tray added={3} ms" -f $results.start.windowMs.median, $results.start.folderListMs.median, $results.start.hotkeyRegisteredMs.median, $results.start.trayAddedMs.median)

        # login: hidden start; hotkey registered, tray added, never a window; then the first chord served
        $logins = @(); $served = @()
        foreach ($i in 1..$Runs) {
            Set-ForegroundByKeyboard $target | Out-Null
            $script:noto = Start-Owner '--login'
            $hotkeyMs = Since $noto 'Noto\.Hotkey registered'
            if ($null -eq $hotkeyMs) { throw "login start ${i}: the hotkey was never registered" }
            if ($i -lt $Runs) {
                # Pressed the moment the hotkey is registered: shown when Noto's UI thread serves it.
                Send-ActivationChord -AllowedForeground $target.Hwnd -NoWait
                if (-not (Until { ($h = Window $noto.Pid) -and (Fg) -eq $h } 30000 5)) { throw "login start ${i}: the first chord was not served" }
                $served += Ms $noto.Created
            }
            $trayMs = Since $noto 'Noto\.Tray added'
            if ($null -eq $trayMs) { throw "login start ${i}: the tray icon was never added" }
            $logins += [pscustomobject]@{ Hotkey = $hotkeyMs; Tray = $trayMs }
            if ($i -lt $Runs) { Stop-Owner; Start-Sleep -Seconds 2 }
            elseif (Window $noto.Pid) { throw "login start $i showed a window" }
        }
        $results.login = [ordered]@{ hotkeyRegisteredMs = Stats ($logins.Hotkey); trayAddedMs = Stats ($logins.Tray); firstChordServedMs = Stats $served }
        Add-Result $run 'login: hidden start; hotkey registered, tray added, first chord served' $true ("median hotkey registered={0} tray added={1} first chord served={2} ms" -f $results.login.hotkeyRegisteredMs.median, $results.login.trayAddedMs.median, $results.login.firstChordServedMs.median)

        # idle, hidden: the last --login start, never shown
        $script:noto.Hwnd = [NotoVal.Win32]::TopWindows([uint32]$noto.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1
        $hiddenIdle = Idle 'hidden'

        # hotkey: from that hidden start, the chord -> the window in front
        $hotkey = @()
        foreach ($i in 1..$Runs) {
            Set-ForegroundByKeyboard $target | Out-Null
            $t0 = Get-Date
            Send-ActivationChord -AllowedForeground $target.Hwnd -NoWait
            if (-not (Until { (Fg) -eq $noto.Hwnd -and [NotoVal.Win32]::IsWindowVisible($noto.Hwnd) } 10000 5)) { throw "hotkey $i did not show Noto" }
            $hotkey += Ms $t0
            Send-ActivationChord -AllowedForeground $noto.Hwnd
            $null = Until { -not [NotoVal.Win32]::IsWindowVisible($noto.Hwnd) } 5000
            Start-Sleep -Milliseconds 500
        }
        $results.hotkey = [ordered]@{ showMs = Stats $hotkey }
        Add-Result $run 'hotkey: chord -> window visible and in front' $true "median $($results.hotkey.showMs.median) ms"

        # idle, shown
        Shown-InFront
        $shownIdle = Idle 'shown'
        $results.idle = @($hiddenIdle, $shownIdle)
        Add-Result $run "idle: $IdleSeconds s hidden and shown" $true ("hidden cpu={0} ms ({1}% of a core) ws={2} MB private={3} MB; shown cpu={4} ms ({5}%) ws={6} MB private={7} MB" -f $hiddenIdle.cpuMs, $hiddenIdle.cpuPercentOfOneCore, $hiddenIdle.workingSetMB, $hiddenIdle.privateMB, $shownIdle.cpuMs, $shownIdle.cpuPercentOfOneCore, $shownIdle.workingSetMB, $shownIdle.privateMB)

        # open, load and save: short and large notes
        foreach ($case in @(@{ Name = 'short'; Id = $SmallId; Content = $Small }, @{ Name = 'large'; Id = $LargeId; Content = $Large })) {
            $opens = @(); $loads = @(); $saves = @()
            foreach ($i in 1..$SaveRuns) {
                $content = Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql 'SELECT Content FROM Notes WHERE Id = $n' -Parameters @{ '$n' = $case.Id }
                $open = Open-Note $case.Id $content
                $opens += $open.Editor; $loads += $open.Loaded
                $saves += Save-ByHide $case.Id $content " edit $i $([guid]::NewGuid().ToString('N').Substring(0, 6))"
                Start-Sleep -Milliseconds 500
            }
            $note = [ordered]@{
                chars = $case.Content.Length; editorPresentMs = Stats $opens; textLoadedMs = Stats $loads
                saveCompletedMs = Stats ($saves.Completed); saveDurationMs = Stats ($saves.Duration); saveObservedMs = Stats ($saves.Observed)
            }
            $results["note_$($case.Name)"] = $note
            Add-Result $run "note ($($case.Name), $($case.Content.Length) chars): open, load, save on hide" $true ("median editor={0} loaded={1}; save completed={2} duration={3} observed={4} ms (n={5})" -f $note.editorPresentMs.median, $note.textLoadedMs.median, $note.saveCompletedMs.median, $note.saveDurationMs.median, $note.saveObservedMs.median, $SaveRuns)
        }
        Stop-Owner
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
    $results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run.Root 'performance.json')
    Write-Host "  performance: $(Join-Path $run.Root 'performance.json')"
}
exit $code
