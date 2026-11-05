#Requires -Version 7.4
<#
.SYNOPSIS
  The v0.1 release baseline (performance and stability slice): the user-visible paths measured on realistic and
  large workspaces, with resource use after each stage. Writes <run>\baseline.json.

.DESCRIPTION
  Two deterministic data roots, built for the run and nothing else:
    S  small: 5 folders, 50 notes.
    L  large: Folders folders holding Notes notes of mixed size (70% short, 25% a page, 5% several pages), some pinned,
       some folded, 200 in the Recycle Bin with a binned folder; and four folders made for measuring:
         "Big"       500 cards
         "Sizes"     Markdown notes of 5, 20, 50, 100 and 250 KB (headings, prose, tasks, code)
         "Pictures"  one note showing Pictures real PNGs (one of them 4000 x 3000), and a note to paste and drop on
         "Zebra"     the notes that hold the rare search words

  Timings are Noto's own, from the lines it traces on the validation channel (read from the debug-output channel and
  stamped on arrival), never from polling the content through UI Automation:
    Noto.Folders shown folders=N ms=      the folder list read and its first frame drawn
    Noto.Stack shown notes=N read-ms= ms= a folder's cards read and their first frame drawn
    Noto.Editor rendered chars=N ms=      a note set into the editor and its first frame drawn
    Noto.Editor change ... ms=            one edit made and restyled (typing, formatting, a paste)
    Noto.Editor images drawn count=N ms=  a note's pictures decoded and drawn
    Noto.Search results=N ms=             one keystroke's query and its results
    Noto.Save completed ms=               a save
  Each is paired with the latency the user sees: from the key (or the process's creation) to the trace's arrival.
  Keys reach Noto only while it is verifiably the foreground window. Typing is Unicode key input.

     1  start: cold (an unbuffered copy of the build, as after an install) on L; warm on S and on L
     2  hotkey: hidden -> shown and in front
     3  a folder opened: 500 cards, ~100 cards, and back to the folder list
     4  scrolling the 500 cards: End to the last card, Home back
     5  the editor at 5, 20, 50, 100 and 250 KB: open, type, format, save, paste over all of it (50 KB and more), leave
     6  search on S and on L (Notes notes): a common word, a rare word, nothing; every keystroke
     7  pictures: a note of Pictures pictures opened; a picture pasted; a picture file dropped
     8  resources after each stage: working set, private bytes, handles, GDI and USER objects, threads

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Baseline.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe, [ValidateRange(3, 50)] [int] $Repeat = 5, [ValidateRange(1000, 100000)] [int] $Notes = 10000, [ValidateRange(10, 1000)] [int] $Folders = 100, [ValidateRange(1, 100)] [int] $Pictures = 20, [int[]] $Only = @(), [int[]] $Sizes = @(5, 20, 50, 100, 250))
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path
$run = New-ValidationRun 'baseline'
Write-Host "run $($run.Id)  exe $NotoExe"
function Want([int] $Stage) { $Only.Count -eq 0 -or $Only -contains $Stage }
$results = [ordered]@{ exe = $NotoExe; machine = $env:COMPUTERNAME; logicalProcessors = [Environment]::ProcessorCount; repeat = $Repeat; notes = $Notes; folders = $Folders; pictures = $Pictures }

# ---------------------------------------------------------------- helpers

. (Join-Path $PSScriptRoot 'helpers/bench.ps1')

# ---------------------------------------------------------------- run

$script:log = [NotoVal.DebugLog]::new()
$script:closes = @()
$resources = [System.Collections.Generic.List[object]]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'v0.1 release baseline' -Body {
    Assert-ChordFree $run
    if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
    $script:target = Start-TargetApp $run
    $small = Join-Path $run.Root 'small'; $large = Join-Path $run.Root 'large'
    New-Item -ItemType Directory $small, $large | Out-Null
    $seedClock = [Diagnostics.Stopwatch]::StartNew()
    Seed $small; Seed $large -Large
    $results.dataset = [ordered]@{ seededMs = $seedClock.ElapsedMilliseconds; largeDbMB = [math]::Round((Get-Item (Join-Path $large 'noto.db')).Length / 1MB, 1); picturesMB = [math]::Round((Get-ChildItem (Join-Path $large 'attachments') -File | Measure-Object Length -Sum).Sum / 1MB, 1) }
    Add-Result $run "0  datasets: S 5 folders / 50 notes; L $Folders+5 folders / $($Notes + 500 + 5 + 5 + 2) notes (+200 binned), $Pictures pictures" $true ("L database {0} MB, pictures {1} MB, seeded in {2} ms" -f $results.dataset.largeDbMB, $results.dataset.picturesMB, $results.dataset.seededMs)

    if (Want 1) {
    # 1 - start
    $coldDir = Join-Path $run.Root 'cold-build'
    $null = robocopy (Split-Path $NotoExe) $coldDir /E /J /R:0 /W:0 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "the unbuffered copy failed (robocopy $LASTEXITCODE)" }
    $null = Set-ForegroundByKeyboard $script:target
    $cold = Start-On $large (Join-Path $coldDir 'Noto.exe'); Stop-Owner
    Remove-Item -LiteralPath $coldDir -Recurse -Force
    $warm = @{}
    foreach ($w in @(@('S', $small), @('L', $large))) {
        $samples = foreach ($i in 1..$Repeat) { $null = Set-ForegroundByKeyboard $script:target; $s = Start-On $w[1]; Start-Sleep -Milliseconds 800; Stop-Owner; $s }
        $warm[$w[0]] = [ordered]@{ foldersShown = Stats ($samples.FoldersShown); foldersShownInner = Stats ($samples.FoldersInner); hotkeyRegistered = Stats ($samples.Hotkey) }
    }
    $results.start = [ordered]@{ cold = [ordered]@{ foldersShown = $cold.FoldersShown; hotkeyRegistered = $cold.Hotkey }; warmSmall = $warm.S; warmLarge = $warm.L; closeMs = Stats $script:closes }
    Add-Result $run '1  start: cold on L; warm on S and on L (process creation -> folder list drawn)' $true ("cold L {0} ms; warm S median {1}, L median {2} (p90 {3}); folder list read+drawn on L {4} ms; hotkey registered L {5} ms; close median {6} ms" -f $cold.FoldersShown, $warm.S.foldersShown.median, $warm.L.foldersShown.median, $warm.L.foldersShown.p90, $warm.L.foldersShownInner.median, $warm.L.hotkeyRegistered.median, (Stats $script:closes).median)

    }
    # The rest runs on L, one process.
    $null = Set-ForegroundByKeyboard $script:target
    $null = Start-On $large
    Front
    $resources.Add((Resources 'large workspace started'))

    if (Want 2) {
    # 2 - hotkey
    $shows = foreach ($i in 1..$Repeat) {
        $null = Set-ForegroundByKeyboard $script:target; $null = Until { -not [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd) } 3000
        $t = Get-Date; Send-ActivationChord -AllowedForeground $script:target.Hwnd -NoWait
        if (-not (Until { (Fg) -eq $script:noto.Hwnd -and [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd) } 5000 2)) { throw 'the hotkey did not show Noto' }
        ((Get-Date) - $t).TotalMilliseconds
    }
    $results.hotkey = Stats $shows
    Add-Result $run '2  hotkey: hidden -> shown and in front' $true (Show 'ms' $results.hotkey)
    Start-Sleep -Milliseconds 400

    }
    if (Want 3) {
    # 3 - folders
    $big = @(); $mid = @(); $back = @()
    foreach ($i in 1..$Repeat) {
        $big += Open-Folder 'Big'
        Focus-Card; $m = Mark; $t = Key @() 0x1B; $null = Until { Element 'FolderList' } 10000 2; $back += ((Get-Date) - $t).TotalMilliseconds
        $mid += Open-Folder 'Folder 010'
    }
    $results.folders = [ordered]@{ big500 = [ordered]@{ drawn = Stats ($big.Inner); seen = Stats ($big.Seen); line = $big[-1].Line }; folder100 = [ordered]@{ drawn = Stats ($mid.Inner); seen = Stats ($mid.Seen); line = $mid[-1].Line }; backToFolders = Stats $back }
    Add-Result $run '3  a folder opened: 500 cards and ~100 cards (Ctrl+Down -> cards drawn), and back' $true ("500: {0} | ~100: {1} | back to folders median {2} ms" -f (Show 'ms' $results.folders.big500.seen), (Show 'ms' $results.folders.folder100.seen), $results.folders.backToFolders.median)
    $resources.Add((Resources 'folders opened'))

    }
    if (Want 4) {
    # 4 - scrolling 500 cards
    $null = Open-Folder 'Big'
    $ends = @(); $homes = @()
    foreach ($i in 1..$Repeat) {
        $first = (Items 'NoteList')[0]; $first.SetFocus(); Start-Sleep -Milliseconds 200
        $t = Key @() 0x23
        if (-not (Until { $f = $AE::FocusedElement; $f -and $f.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $f.Current.Name -match '^\S+ 3500\b|^# \S+ 3500\b|3500' } 10000 5)) { $ends += -1 } else { $ends += ((Get-Date) - $t).TotalMilliseconds }
        $t = Key @() 0x24
        $null = Until { $f = $AE::FocusedElement; $f -and $f.Current.Name -match '\b7\b' } 10000 5; $homes += ((Get-Date) - $t).TotalMilliseconds
    }
    $results.scroll = [ordered]@{ toLast = Stats $ends; toFirst = Stats $homes }
    Add-Result $run '4  scrolling 500 cards: End -> the last card focused, Home -> the first' (@($ends | Where-Object { $_ -lt 0 }).Count -eq 0) ("End {0} | Home {1}" -f (Show 'ms' $results.scroll.toLast), (Show 'ms' $results.scroll.toFirst))
    $resources.Add((Resources 'scrolled'))

    }
    if (Want 5) {
    # 5 - the editor by size
    $results.editor = [ordered]@{}
    $sizes = $Sizes
    $order = @(5, 20, 50, 100, 250)
    foreach ($kb in $sizes) {
        $s = [Array]::IndexOf($order, $kb)
        $opens = @(); $openSeen = @(); $typing = @(); $typingSeen = @(); $formats = @(); $saves = @(); $pastes = @(); $pasteSeen = @(); $leaves = @()
        foreach ($i in 1..$Repeat) {
            $null = Open-Folder 'Sizes'
            $o = Open-Card $s "Size $kb KB"; $opens += $o.Inner; $openSeen += $o.Seen
            # type: ten characters at the end of the note
            $null = Key @(0x11) 0x23; Start-Sleep -Milliseconds 300
            foreach ($ch in 'perf test '.ToCharArray()) { $m = Mark; $t = Char $ch; $line = Next $m 'Noto\.Editor change chars='; $typing += Inner $line; $typingSeen += Since $t $line }
            # format: the last word made bold
            $null = Key @(0x11, 0x10) 0x25; Start-Sleep -Milliseconds 200
            $m = Mark; $t = Key @(0x11) 0x42; $formats += Inner (Next $m 'Noto\.Editor change chars=')
            # save: hidden with the hotkey, which saves first; then shown again
            $m = Mark; Send-ActivationChord -AllowedForeground $script:noto.Hwnd -NoWait; $saves += Inner (Next $m 'Noto\.Save completed ms='); Front
            # paste over all of it (the large-change path), from 50 KB up
            if ($kb -ge 50) {
                $current = Get-NotoScalar $run -Exe $NotoExe -DataRoot $large -Sql 'SELECT Content FROM Notes WHERE Id = $n' -Parameters @{ '$n' = ('01J0000000Z' + $kb.ToString('D15')) }
                Set-Clipboard -Value ($(if ($i % 2) { $current.ToUpperInvariant() } else { $current.ToLowerInvariant() })); Start-Sleep -Milliseconds 500
                $null = Key @(0x11) 0x41; Start-Sleep -Milliseconds 200
                $m = Mark; $t = Key @(0x11) 0x56; $line = Next $m 'Noto\.Editor change chars='
                $pastes += Inner $line; $pasteSeen += Since $t $line
            }
            # leave: back to the cards (no re-read: the card shows the stored text), to the first frame drawn after it
            $m = Mark; $t = Key @() 0x1B; $line = Next $m 'Noto\.Editor left chars='; $leaves += Since $t $line
        }
        $results.editor["kb$kb"] = [ordered]@{ open = Stats $opens; openSeen = Stats $openSeen; keystroke = Stats $typing; keystrokeSeen = Stats $typingSeen; format = Stats $formats; save = Stats $saves; pasteOverAll = Stats $pastes; pasteOverAllSeen = Stats $pasteSeen; leave = Stats $leaves }
        $e = $results.editor["kb$kb"]
        Add-Result $run ("5  editor {0} KB: open, type, format, save, paste over all, leave" -f $kb) $true ("open drawn {0} (Enter->drawn {1}) | key {2} (seen {3}) | bold {4} | save {5} | paste-all {6} | leave {7} ms (medians; key p90 seen {8})" -f $e.open.median, $e.openSeen.median, $e.keystroke.median, $e.keystrokeSeen.median, $e.format.median, $e.save.median, $(if ($e.pasteOverAll.n) { $e.pasteOverAllSeen.median } else { '-' }), $e.leave.median, $e.keystrokeSeen.p90)
        $resources.Add((Resources "editor $kb KB"))
    }

    }
    if (Want 6) {
    # 6 - search
    $results.search = [ordered]@{}
    foreach ($w in @(@('L', $large))) {
        foreach ($q in 'budget', 'zebra7003', 'qxqxqx') {
            $inner = @(); $seen = @(); $counts = @()
            foreach ($i in 1..$Repeat) {
                To-Folders
                $null = Key @(0x11) 0x46; $null = Until { Element 'SearchBox' } 5000; Start-Sleep -Milliseconds 300
                foreach ($ch in $q.ToCharArray()) { $m = Mark; $t = Char $ch; $line = Next $m 'Noto\.Search results='; $inner += Inner $line; $seen += Since $t $line; if ($line -match 'results=(\d+)') { $counts += [int]$Matches[1] } }
                $null = Key @() 0x1B; $null = Until { -not (Element 'SearchBox') } 5000
            }
            $results.search["L_$q"] = [ordered]@{ keystroke = Stats $inner; keystrokeSeen = Stats $seen; finalResults = $counts[-1] }
            Add-Result $run "6  search L ($Notes notes) '$q', every keystroke" $true ("query {0} | key->results {1} | {2} results" -f (Show 'ms' (Stats $inner)), (Show 'ms' (Stats $seen)), $counts[-1])
        }
    }
    $resources.Add((Resources 'searched'))

    }
    if (Want 7) {
    # 7 - pictures
    $opens = @(); $drawn = @()
    foreach ($i in 1..$Repeat) {
        $null = Open-Folder 'Pictures'
        $o = Open-Card 0; $opens += $o.Seen
        $line = Next $o.Mark 'Noto\.Editor images drawn count='; $drawn += Inner $line
        $null = Key @() 0x1B; $null = Until { -not (Element 'NoteEditor') } 10000
    }
    $null = Open-Folder 'Pictures'; $null = Open-Card 1; $null = Key @(0x11) 0x23; Start-Sleep -Milliseconds 300
    $savedClip = Get-Clipboard -Raw
    $pasteMs = @()
    try {
        foreach ($i in 1..3) {
            Invoke-Sta 'set-image' $SetImage @('2400', '1600')
            $m = Mark; $t = Key @(0x11) 0x56; $line = Next $m 'Noto\.Editor change chars='; $pasteMs += Since $t $line
            $null = Key @() 0x0D; Start-Sleep -Milliseconds 300
        }
    }
    finally { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } }
    $results.pictures = [ordered]@{ count = $Pictures; open = Stats $opens; drawn = Stats $drawn; paste2400x1600 = Stats $pasteMs }
    Add-Result $run "7  pictures: a note of $Pictures pictures opened and drawn; a 2400x1600 picture pasted" $true ("open {0} | all drawn {1} | paste->in the note {2}" -f (Show 'ms' $results.pictures.open), (Show 'ms' $results.pictures.drawn), (Show 'ms' $results.pictures.paste2400x1600))
    $null = Key @() 0x1B
    $resources.Add((Resources 'pictures'))

    }
    # 8 - resources
    $results.resources = $resources
    $first = $resources[0]; $last = $resources[$resources.Count - 1]
    Add-Result $run '8  resources after each stage (working set, private, handles, GDI, USER)' $true (($resources | ForEach-Object { "$($_.stage): ws $($_.workingSetMB) priv $($_.privateMB) h $($_.handles) gdi $($_.gdi) user $($_.user)" }) -join ' | ')
    Stop-Owner
}) }
finally {
    @($script:log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $script:log.Dispose()
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run.Root 'baseline.json')
    Write-Host "  baseline: $(Join-Path $run.Root 'baseline.json')"
}
