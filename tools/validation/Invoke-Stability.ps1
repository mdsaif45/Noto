#Requires -Version 7.4
<#
.SYNOPSIS
  The v0.1 stability campaign (performance and stability slice): repeated real use of a frozen build, watched for
  crashes, hangs, slow closes, data loss and resource growth. Writes <run>\stability.json.

.DESCRIPTION
  One deterministic data root ("L" of Invoke-Baseline.ps1 at -Notes notes; default 3,000 in 30 folders, with the 500-card
  "Big", the "Sizes" notes of 5 to 250 KB, "Pictures" and a Recycle Bin). Keys reach Noto only while it is verifiably
  the foreground window. After every step of every loop the window's thread must answer a message within 2 s (WM_NULL,
  SMTO_ABORTIFHUNG): a step that does not is a hang, and fails the run. Resources (working set, private bytes, handles,
  GDI and USER objects, threads) are sampled at each loop's start and end, and every tenth step of the long ones.

     1  open/close: Cycles starts and closes of the whole application; every close clean, timed
     2  open, edit, save, close: Cycles times a note opened, typed into, left (which saves); every edit in the database
     3  hide/show: Toggles hotkey presses, alternately hiding and showing; every state reached
     4  note switching: Switches notes opened in turn in "Sizes" and "Big" (5 KB to 250 KB, short cards)
     5  folder switching: Switches folders entered and left
     6  search while typing: Cycles queries typed a key at a time, each closed
     7  pictures: Cycles opens of the note of pictures, each drawn
     8  forced termination: Kills times Noto killed while a note is being edited, then started again: the database
        intact (integrity_check), the text autosaved before the kill present, the folder list shown
     9  resources over the session: start, every checkpoint, end; growth reported per stage
    10  crashes and hangs: the Application log's crash events for Noto during the run; hangs seen

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Stability.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe, [ValidateRange(3, 500)] [int] $Cycles = 20, [ValidateRange(10, 2000)] [int] $Toggles = 100, [ValidateRange(10, 2000)] [int] $Switches = 60, [ValidateRange(1, 50)] [int] $Kills = 5, [ValidateRange(1000, 100000)] [int] $Notes = 3000, [ValidateRange(10, 1000)] [int] $Folders = 30, [ValidateRange(1, 100)] [int] $Pictures = 10)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path
$run = New-ValidationRun 'stability'
Write-Host "run $($run.Id)  exe $NotoExe"
$results = [ordered]@{ exe = $NotoExe; machine = $env:COMPUTERNAME; cycles = $Cycles; toggles = $Toggles; switches = $Switches; kills = $Kills; notes = $Notes; folders = $Folders; pictures = $Pictures }

. (Join-Path $PSScriptRoot 'helpers/bench.ps1')

$script:hangs = [System.Collections.Generic.List[string]]::new()
function Alive { [bool](Get-Process -Id $script:noto.Pid -ErrorAction SilentlyContinue) }
function Check([string] $Step) {
    if (-not (Alive)) { throw "Noto ended during: $Step" }
    if (-not [NotoVal.Gui]::Responds($script:noto.Hwnd, 2000)) { $script:hangs.Add($Step); throw "Noto did not answer within 2 s after: $Step" }
}
$script:samples = [System.Collections.Generic.List[object]]::new()
function Sample([string] $Stage) { $r = Resources $Stage; $script:samples.Add($r); $r }
function Growth([string] $Stage) {
    $s = @($script:samples | Where-Object { $_.stage -like "$Stage*" })
    if ($s.Count -lt 2) { return 'n/a' }
    $a = $s[0]; $b = $s[-1]
    "ws {0:+0.0;-0.0} MB, private {1:+0.0;-0.0} MB, handles {2:+0;-0}, gdi {3:+0;-0}, user {4:+0;-0}" -f ($b.workingSetMB - $a.workingSetMB), ($b.privateMB - $a.privateMB), ($b.handles - $a.handles), ($b.gdi - $a.gdi), ($b.user - $a.user)
}
function Db([string] $Sql, [hashtable] $P = @{}) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $script:data -Sql $Sql -Parameters $P }

$script:log = [NotoVal.DebugLog]::new()
$script:closes = @()
$since = Get-Date
try {
exit (Invoke-IsolatedRun $run -Title 'v0.1 stability campaign' -Body {
    Assert-ChordFree $run
    if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    Seed $script:data -Large

    # 1 - open/close
    $starts = @()
    foreach ($i in 1..$Cycles) {
        $null = Set-ForegroundByKeyboard $script:target
        $s = Start-On $script:data; $starts += $s.FoldersShown
        if ($i -eq 1 -or $i -eq $Cycles) { $null = Sample "open-close $i" }
        Check "start $i"
        Stop-Owner
    }
    $results.openClose = [ordered]@{ start = Stats $starts; close = Stats $script:closes }
    Add-Result $run "1  open/close $Cycles times: every close clean" $true ("start {0} | close {1}" -f (Show 'ms' $results.openClose.start), (Show 'ms' $results.openClose.close))

    # One long session for the rest.
    $null = Set-ForegroundByKeyboard $script:target
    $null = Start-On $script:data
    Front
    $null = Sample 'session start'

    # 2 - open, edit, save, close
    $saved = 0
    foreach ($i in 1..$Cycles) {
        $null = Open-Folder 'Folder 001'; Check "folder $i"
        $null = Open-Card ($i % 5); Check "open $i"
        $null = Key @(0x11) 0x23
        $word = "edit$i"; foreach ($ch in $word.ToCharArray()) { $null = Char $ch }
        $null = Key @() 0x1B; $null = Until { -not (Element 'NoteEditor') } 20000 10; Check "leave $i"
        $null = Until { (Db 'SELECT COUNT(*) FROM Notes WHERE Content LIKE $w' @{ '$w' = "%$word" }) -ge 1 } 10000 100
        if ((Db 'SELECT COUNT(*) FROM Notes WHERE Content LIKE $w' @{ '$w' = "%$word" }) -ge 1) { $saved++ }
        if ($i % 10 -eq 0) { $null = Sample "edit $i" }
    }
    Add-Result $run "2  open, edit, save, close $Cycles times: every edit in the database" ($saved -eq $Cycles) ("saved {0}/{1}; {2}" -f $saved, $Cycles, (Growth 'edit'))

    # 3 - hide/show
    $reached = 0
    foreach ($i in 1..$Toggles) {
        $shown = [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd)
        if ($shown) { if ((Fg) -ne $script:noto.Hwnd) { Front }; Send-ActivationChord -AllowedForeground $script:noto.Hwnd -NoWait }
        else { $null = Set-ForegroundByKeyboard $script:target; Send-ActivationChord -AllowedForeground $script:target.Hwnd -NoWait }
        if (Until { [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd) -ne $shown } 5000 10) { $reached++ }
        Start-Sleep -Milliseconds 300
        Check "toggle $i"
        if ($i % 20 -eq 0) { $null = Sample "toggle $i" }
    }
    if (-not [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd)) { Front }
    Add-Result $run "3  hide/show $Toggles times: every state reached" ($reached -eq $Toggles) ("reached {0}/{1}; {2}" -f $reached, $Toggles, (Growth 'toggle'))

    # 4 - note switching
    $opened = 0
    foreach ($i in 1..$Switches) {
        $folder = if ($i % 2) { 'Sizes' } else { 'Big' }
        $null = Open-Folder $folder
        $o = if ($folder -eq 'Sizes') { $kb = @(5, 20, 50, 100, 250)[$i % 5]; Open-Card ($i % 5) "Size $kb KB" } else { Open-Card ($i % 5) }; $opened++
        Check "note $i ($folder)"
        $null = Key @() 0x1B; $null = Until { -not (Element 'NoteEditor') } 20000 10
        if ($i % 10 -eq 0) { $null = Sample "switch $i" }
    }
    Add-Result $run "4  note switching $Switches times (5 KB to 250 KB, short cards)" ($opened -eq $Switches) ("opened {0}; {1}" -f $opened, (Growth 'switch'))

    # 5 - folder switching
    $entered = 0
    foreach ($i in 1..$Switches) {
        $null = Open-Folder ("Folder {0:000}" -f (1 + ($i % [Math]::Min(10, $Folders)))); $entered++
        Check "folder $i"
        if ($i % 10 -eq 0) { $null = Sample "folders $i" }
    }
    Add-Result $run "5  folder switching $Switches times" ($entered -eq $Switches) ("entered {0}; {1}" -f $entered, (Growth 'folders'))

    # 6 - search while typing
    $words = 'meeting', 'budget', 'roadmap', 'invoice', 'travel', 'design', 'review', 'zebra7003', 'qxqx', 'launch'
    $answered = 0
    foreach ($i in 1..$Cycles) {
        To-Folders
        $null = Key @(0x11) 0x46; $null = Until { Element 'SearchBox' } 5000; Start-Sleep -Milliseconds 200
        $q = $words[$i % $words.Count]
        $m = Mark; foreach ($ch in $q.ToCharArray()) { $null = Char $ch; Start-Sleep -Milliseconds 40 }
        if ($null -ne (Next $m 'Noto\.Search results=' 10000)) { $answered++ }
        Check "search $i"
        $null = Key @() 0x1B; $null = Until { -not (Element 'SearchBox') } 5000
        if ($i % 10 -eq 0) { $null = Sample "search $i" }
    }
    Add-Result $run "6  search while typing $Cycles queries" ($answered -eq $Cycles) ("answered {0}; {1}" -f $answered, (Growth 'search'))

    # 7 - pictures
    $drawn = 0
    foreach ($i in 1..$Cycles) {
        $null = Open-Folder 'Pictures'
        $o = Open-Card 0
        if (Next $o.Mark 'Noto\.Editor images drawn count=' 30000) { $drawn++ }
        Check "pictures $i"
        $null = Key @() 0x1B; $null = Until { -not (Element 'NoteEditor') } 20000 10
        if ($i % 10 -eq 0) { $null = Sample "pictures $i" }
    }
    Add-Result $run "7  pictures opened $Cycles times, each drawn" ($drawn -eq $Cycles) ("drawn {0}; {1}" -f $drawn, (Growth 'pictures'))
    $null = Sample 'session end'
    Stop-Owner

    # 8 - forced termination
    $intact = 0; $kept = 0; $shown = 0
    foreach ($i in 1..$Kills) {
        $null = Set-ForegroundByKeyboard $script:target
        $null = Start-On $script:data; Front
        $null = Open-Folder 'Folder 002'; $null = Open-Card 0; $null = Key @(0x11) 0x23
        $word = "kill$i"; foreach ($ch in $word.ToCharArray()) { $null = Char $ch }
        $null = Next (Mark) 'Noto\.Save completed' 10000   # the autosave
        foreach ($ch in 'unsaved'.ToCharArray()) { $null = Char $ch }
        Stop-Process -Id $script:noto.Pid -Force; $null = Until { -not (Alive) } 5000
        if ((Db 'PRAGMA integrity_check') -eq 'ok') { $intact++ }
        if ((Db 'SELECT COUNT(*) FROM Notes WHERE Content LIKE $w' @{ '$w' = "%$word%" }) -ge 1) { $kept++ }
        $null = Set-ForegroundByKeyboard $script:target
        $s = Start-On $script:data; if ($s.FoldersShown -gt 0) { $shown++ }
        Check "restart after kill $i"
        Stop-Owner
    }
    Add-Result $run "8  killed while editing $Kills times, then started: database intact, autosaved text kept, folders shown" ($intact -eq $Kills -and $kept -eq $Kills -and $shown -eq $Kills) ("intact {0}, kept {1}, shown {2} of {3}" -f $intact, $kept, $shown, $Kills)

    # 9 - resources
    $results.resources = $script:samples
    $first = $script:samples | Where-Object stage -eq 'session start' | Select-Object -First 1
    $last = $script:samples | Where-Object stage -eq 'session end' | Select-Object -First 1
    $results.sessionGrowth = [ordered]@{ workingSetMB = $last.workingSetMB - $first.workingSetMB; privateMB = $last.privateMB - $first.privateMB; handles = $last.handles - $first.handles; gdi = [int]$last.gdi - [int]$first.gdi; user = [int]$last.user - [int]$first.user }
    Add-Result $run '9  resources over the session (start -> end; see stability.json for every checkpoint)' $true ("start ws {0} priv {1} h {2} gdi {3} user {4} -> end ws {5} priv {6} h {7} gdi {8} user {9}" -f $first.workingSetMB, $first.privateMB, $first.handles, $first.gdi, $first.user, $last.workingSetMB, $last.privateMB, $last.handles, $last.gdi, $last.user)

    # 10 - crashes and hangs
    $crashes = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000, 1001, 1002, 1026; StartTime = $since } -ErrorAction SilentlyContinue | Where-Object { $_.Message -match 'Noto\.exe' })
    $results.crashEvents = $crashes.Count; $results.hangs = @($script:hangs)
    Add-Result $run '10 no crash or hang event for Noto during the run (Application log 1000, 1001, 1002, 1026)' ($crashes.Count -eq 0 -and $script:hangs.Count -eq 0) ("crash events {0}, hangs {1}" -f $crashes.Count, $script:hangs.Count)
}) }
finally {
    @($script:log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $script:log.Dispose()
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run.Root 'stability.json')
    Write-Host "  stability: $(Join-Path $run.Root 'stability.json')"
}
