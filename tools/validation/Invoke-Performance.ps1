#Requires -Version 7.4
<#
.SYNOPSIS
  Noto's performance baseline (#11): measured on the real application, repeatable, recorded, so a regression shows.

.DESCRIPTION
  One isolated data root, seeded at a realistic size: NoteCount notes (default 1000) across 20 folders, with the
  measured folder holding a short note, a large note (a deterministic ~100 KB Markdown-style document) and 48 more. Noto is started by the shell, as a user starts it, on a temporary virtual desktop. Every timing is taken
  from outside the process, by polling what a user would see: a window becoming visible, an element appearing in UI
  Automation, a row changing in the database. Polling resolves to about 10-30 ms, plus UI Automation's own cost; the
  numbers are comparable between runs on one machine, not absolute.

    cold       The run's first start, before anything else of Noto's has run: process creation (the kernel's own
               time) -> the window visible, and -> tray and hotkey ready (Noto traces "Noto.Tray added"). It is a cold
               start only when the run is the first Noto start since Windows started (the file cache holds none of
               Noto's binaries); the run records the boot time and its own start so that can be checked. It also
               creates the database of a new data root.
    start      N warm starts on the seeded data root: process creation -> the window visible -> the folder list
               populated, and -> tray and hotkey ready.
    login      N hidden --login starts: process creation -> tray and hotkey ready, never a window.
    hotkey     From a hidden --login start: the activation chord -> the window visible and in front.
    idle       Hidden, then shown: CPU over IdleSeconds (ms of CPU, and % of one core), working set and private bytes.
    open/load  A note opened from the list: Enter -> the editor present -> the editor holding the note's text. Short
               and large notes.
    save       Text changed, then the workspace hidden with the hotkey (which saves first): the chord -> the row in
               the database holding the new text. Short and large notes.

  Results are printed and written to <run>\performance.json. No developer-only tool is needed: PowerShell 7 and the
  built Noto.exe.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Performance.ps1 -NotoExe <path>\Noto.exe [-Runs 5] [-IdleSeconds 60]
#>
param([Parameter(Mandatory)] [string] $NotoExe, [ValidateRange(2, 50)] [int] $Runs = 5, [ValidateRange(10, 3600)] [int] $IdleSeconds = 60, [ValidateRange(50, 100000)] [int] $NoteCount = 1000)
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
    $sorted = @($Values | Sort-Object)
    [ordered]@{ n = $sorted.Count; min = $sorted[0]; median = $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]; max = $sorted[-1]; values = $sorted }
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
function Start-Owner([string] $Switches = '') {
    $p = Start-ViaShell $run -File $NotoExe -Arguments "--data-root=`"$data`" $Switches".Trim() -Marker "--data-root=`"$data`" $Switches".Trim()
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
    # C4; Enter is not bound on the folder list).
    if (-not (Element $noto.Hwnd 'NoteList')) {
        if (-not (Until { ($script:folder = FirstItem $noto.Hwnd 'FolderList') } 10000)) { throw 'no folder to select' }
        $script:folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $script:folder.SetFocus(); Start-Sleep -Milliseconds 200
        Keys-ToNoto 0x28 -Ctrl
    }
    $script:items = @()
    $entered = Until { $l = Element $noto.Hwnd 'NoteList'; if ($l) { $script:items = @($l.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) }; $script:items.Count -ge 2 } 10000
    if (-not $entered) { throw "Ctrl+Down did not open the folder (notes listed: $($script:items.Count))" }
    $index = if ($NoteId -eq $LargeId) { 1 } else { 0 }   # sort order: the short note first
    $item = $script:items[$index]
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus(); Start-Sleep -Milliseconds 300
    if ((Fg) -ne $noto.Hwnd) { Shown-InFront; $item.SetFocus(); Start-Sleep -Milliseconds 200 }
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
    $t0 = Get-Date
    Send-ActivationChord -AllowedForeground $noto.Hwnd -NoWait
    $saved = Until { (Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql 'SELECT substr(Content, -length($s)) = $s FROM Notes WHERE Id = $n' -Parameters @{ '$n' = $NoteId; '$s' = $Suffix }) -eq 1 } 60000 5
    $ms = Ms $t0
    if (-not $saved) { throw 'the hide did not save the note' }
    $ms
}
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

        # cold: the first start of Noto in this run, on the new data root (it creates the database)
        $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
        Set-ForegroundByKeyboard $target | Out-Null
        $script:noto = Start-Owner
        $coldVisible = Until { Window $noto.Pid } 60000 5; $coldWindowMs = Ms $noto.Created
        $null = Until { Traced $noto.Pid 'Noto\.Tray added' } 60000
        $coldTray = Traced $noto.Pid 'Noto\.Tray added'
        if (-not ($coldVisible -and $coldTray)) { throw "the first start was not measured (visible=$coldVisible tray=$([bool]$coldTray))" }
        $results.cold = [ordered]@{ windowMs = $coldWindowMs; trayReadyMs = [math]::Round(($coldTray - $noto.Created).TotalMilliseconds); bootTime = $boot.ToString('O'); minutesSinceBoot = [math]::Round(($noto.Created - $boot).TotalMinutes, 1) }
        Stop-Owner
        Add-Result $run 'cold: the run''s first start, window visible and tray ready' $true ("window={0} tray={1} ms; {2} min after boot" -f $results.cold.windowMs, $results.cold.trayReadyMs, $results.cold.minutesSinceBoot)
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

        # start: window visible, folder list populated, tray and hotkey ready
        $starts = @()
        foreach ($i in 1..$Runs) {
            Set-ForegroundByKeyboard $target | Out-Null
            $script:noto = Start-Owner
            $visible = Until { Window $noto.Pid } 30000 5; $windowMs = Ms $noto.Created
            $script:noto.Hwnd = Window $noto.Pid
            $usable = Until { FirstItem $noto.Hwnd 'FolderList' } 30000 5; $usableMs = Ms $noto.Created
            $null = Until { Traced $noto.Pid 'Noto\.Tray added' } 30000
            $tray = Traced $noto.Pid 'Noto\.Tray added'
            $starts += [pscustomobject]@{ Window = $windowMs; Usable = $usableMs; Tray = [math]::Round(($tray - $noto.Created).TotalMilliseconds) }
            if (-not ($visible -and $usable -and $tray)) { throw "start $i was not measured (visible=$visible usable=$usable tray=$([bool]$tray))" }
            Stop-Owner
            Start-Sleep -Seconds 2
        }
        $results.start = [ordered]@{ windowMs = Stats ($starts.Window); usableMs = Stats ($starts.Usable); trayReadyMs = Stats ($starts.Tray) }
        Add-Result $run 'start: window visible, folder list populated, tray ready' $true ("median window={0} usable={1} tray={2} ms" -f $results.start.windowMs.median, $results.start.usableMs.median, $results.start.trayReadyMs.median)

        # login: hidden start, tray and hotkey ready, never a window
        $logins = @()
        foreach ($i in 1..$Runs) {
            Set-ForegroundByKeyboard $target | Out-Null
            $script:noto = Start-Owner '--login'
            if (-not (Until { Traced $noto.Pid 'Noto\.Tray added' } 30000)) { throw "login start ${i}: the tray never became ready" }
            $logins += [math]::Round(((Traced $noto.Pid 'Noto\.Tray added') - $noto.Created).TotalMilliseconds)
            $shown = [bool](Window $noto.Pid)
            if ($i -lt $Runs) { Stop-Owner; Start-Sleep -Seconds 2 }
            if ($shown) { throw "login start $i showed a window" }
        }
        $results.login = [ordered]@{ trayReadyMs = Stats $logins }
        Add-Result $run 'login: hidden start, tray and hotkey ready, no window' $true "median $($results.login.trayReadyMs.median) ms"

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
            foreach ($i in 1..$Runs) {
                $content = Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql 'SELECT Content FROM Notes WHERE Id = $n' -Parameters @{ '$n' = $case.Id }
                $open = Open-Note $case.Id $content
                $opens += $open.Editor; $loads += $open.Loaded
                $saves += Save-ByHide $case.Id $content " edit $i $([guid]::NewGuid().ToString('N').Substring(0, 6))"
                Start-Sleep -Milliseconds 500
            }
            $results["note_$($case.Name)"] = [ordered]@{ chars = $case.Content.Length; editorPresentMs = Stats $opens; textLoadedMs = Stats $loads; saveOnHideMs = Stats $saves }
            Add-Result $run "note ($($case.Name), $($case.Content.Length) chars): open, load, save on hide" $true ("median editor={0} loaded={1} save={2} ms" -f $results["note_$($case.Name)"].editorPresentMs.median, $results["note_$($case.Name)"].textLoadedMs.median, $results["note_$($case.Name)"].saveOnHideMs.median)
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
