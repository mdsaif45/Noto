# Shared by Invoke-Baseline.ps1 and Invoke-Stability.ps1, dot-sourced into the script's scope: they use its $run,
# $NotoExe, $script:noto, $script:log and $script:target. Timings are Noto's own traces; keys reach Noto only while it is
# the foreground window.
$AE = [System.Windows.Automation.AutomationElement]; $Scope = [System.Windows.Automation.TreeScope]
$ListItem = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
if (-not ('NotoVal.Gui' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace NotoVal { public static class Gui {
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    // WM_NULL with SMTO_ABORTIFHUNG: whether the window's thread answers within the timeout.
    public static bool Responds(IntPtr h, uint timeoutMs) { IntPtr r; return SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 0x0002, timeoutMs, out r) != IntPtr.Zero; }
} }
'@
}


function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Until([scriptblock] $Probe, [int] $TimeoutMs = 30000, [int] $PollMs = 10) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) { if (& $Probe) { return $true }; Start-Sleep -Milliseconds $PollMs }
    $false
}
function Element([string] $Id) { try { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) } catch { $null } }
function Items([string] $ListId) { $l = Element $ListId; if ($l) { @($l.FindAll($Scope::Descendants, $ListItem)) } else { @() } }
function Stats([double[]] $Values) {
    $sorted = @($Values | Sort-Object); $n = $sorted.Count
    if ($n -eq 0) { return [ordered]@{ n = 0 } }
    $mid = [int][math]::Floor(($n - 1) / 2)
    $median = if ($n % 2) { $sorted[$mid] } else { ($sorted[$mid] + $sorted[$mid + 1]) / 2 }
    $p90 = $sorted[[int][math]::Min($n - 1, [math]::Ceiling(0.9 * $n) - 1)]
    [ordered]@{ n = $n; min = [math]::Round($sorted[0], 2); median = [math]::Round($median, 2); p90 = [math]::Round($p90, 2); max = [math]::Round($sorted[-1], 2) }
}
if ((Stats @(4, 1)).median -ne 2.5 -or (Stats @(3, 1, 2)).median -ne 2 -or (Stats @(1..10)).p90 -ne 9) { throw 'Stats is wrong' }
function Show([string] $Label, $S) { "{0} n={1} median={2} p90={3} max={4}" -f $Label, $S.n, $S.median, $S.p90, $S.max }

# The trace: every line Noto writes, stamped on arrival. A measure waits for the next line matching after a mark.
function Lines { @($script:log.Lines($script:noto.Pid)) }
function Mark { (Lines).Count }
function Stamp([string] $Line) {
    if ($Line -notmatch '^(\d\d:\d\d:\d\d\.\d\d\d) ') { throw "unstamped trace line: $Line" }
    $t = [datetime]::ParseExact((Get-Date -Format 'yyyy-MM-dd') + ' ' + $Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null)
    if (($t - (Get-Date)).TotalHours -gt 12) { $t = $t.AddDays(-1) }
    $t
}
function Next([int] $After, [string] $Pattern, [int] $TimeoutMs = 60000) {
    $script:found = $null
    $ok = Until { $all = Lines; for ($i = $After; $i -lt $all.Count; $i++) { if ($all[$i] -match $Pattern) { $script:found = $all[$i]; return $true } }; $false } $TimeoutMs 5
    if (-not $ok) { throw "no trace matching '$Pattern' within $TimeoutMs ms (alive=$([bool](Get-Process -Id $script:noto.Pid -ErrorAction SilentlyContinue)))" }
    $script:found
}
function Inner([string] $Line) { if ($Line -match ' ms=([0-9.]+)') { [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) } else { throw "no ms= in: $Line" } }
function Since([datetime] $From, [string] $Line) { [math]::Round(((Stamp $Line) - $From).TotalMilliseconds, 1) }

function Key([int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $script:noto.Hwnd) { throw 'refusing keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    $t = Get-Date
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    $t
}
function Char([char] $C) {
    if ((Fg) -ne $script:noto.Hwnd) { throw 'refusing to type: Noto is not the foreground window' }
    $t = Get-Date
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Uni($C, $false), [NotoVal.Win32]::Uni($C, $true)))
    $t
}
function Front {
    for ($a = 1; $a -le 4; $a++) {
        $null = Set-ForegroundByKeyboard $script:target
        if ((Fg) -eq $script:target.Hwnd) { Send-ActivationChord -AllowedForeground $script:target.Hwnd }
        if (Until { (Fg) -eq $script:noto.Hwnd } 3000) { Start-Sleep -Milliseconds 400; return }
    }
    throw 'the hotkey did not bring Noto forward'
}
function Resources([string] $Stage) {
    $p = Get-Process -Id $script:noto.Pid; $p.Refresh()
    [ordered]@{ stage = $Stage; workingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); privateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); handles = $p.HandleCount; gdi = [NotoVal.Gui]::GetGuiResources($p.Handle, 0); user = [NotoVal.Gui]::GetGuiResources($p.Handle, 1); threads = $p.Threads.Count }
}

# Back to the folder list from wherever the workspace is: the editor, a note list, Search. Escape acts where the
# keyboard is, so a card (or the editor) holds focus first, as it does for a user.
function Focus-Card { $c = @(Items 'NoteList')[0]; if ($c -and -not (Element 'NoteEditor')) { try { $c.SetFocus(); Start-Sleep -Milliseconds 150 } catch { } } }
function To-Folders {
    for ($i = 0; $i -lt 4 -and -not (Element 'FolderList'); $i++) { Focus-Card; $null = Key @() 0x1B; $null = Until { Element 'FolderList' } 2000 }
    if (-not (Element 'FolderList')) { throw 'could not return to the folder list' }
}
function Open-Folder([string] $Name) {
    To-Folders
    $script:row = $null
    $null = Until { $script:row = @(Items 'FolderList' | Where-Object { $_.Current.Name -eq $Name })[0]; if (-not $script:row) { $l = Element 'FolderList'; if ($l) { try { $l.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Scroll('NoAmount', 'LargeIncrement') } catch { } } }; $script:row } 20000 50
    if (-not $script:row) { throw "no folder '$Name' in the list" }
    # Selected and focused, and focus confirmed on that row, before Ctrl+Down: a virtualised row realised a moment ago
    # can lose a focus given too early.
    $held = Until {
        try {
            $script:row = @(Items 'FolderList' | Where-Object { $_.Current.Name -eq $Name })[0]
            $script:row.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
            $script:row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $script:row.SetFocus()
            Start-Sleep -Milliseconds 150
            $f = $AE::FocusedElement; $f -and $f.Current.Name -eq $Name -and (Fg) -eq $script:noto.Hwnd
        } catch { $false }
    } 10000 100
    if (-not $held) { throw "the folder '$Name' did not keep keyboard focus" }
    $m = Mark; $t = Key @(0x11) 0x28
    $line = Next $m 'Noto\.Stack shown notes='
    [pscustomobject]@{ Inner = Inner $line; Seen = Since $t $line; Line = $line }
}
function Open-Card([int] $Index, [string] $Name = '') {
    # Reached as a user reaches it: the first card focused, then Down until the card is the one asked for (by $Name when
    # given, a pattern of its title), then Enter. A list of large cards realises only the ones in view, so a later card is
    # not in UI Automation until focus brings it there.
    $script:first = $null; $null = Until { $script:first = @(Items 'NoteList')[0]; $script:first } 20000 50
    $script:first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $script:first.SetFocus(); Start-Sleep -Milliseconds 250
    $want = { $f = $AE::FocusedElement; $f -and $f.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and ($Name -eq '' -or $f.Current.Name -match $Name) }
    if ($Name) { for ($k = 0; $k -lt $Index + 6 -and -not (& $want); $k++) { $null = Key @() 0x28; Start-Sleep -Milliseconds 250 } }
    else { for ($k = 0; $k -lt $Index; $k++) { $null = Key @() 0x28; Start-Sleep -Milliseconds 250 } }
    if (-not (& $want)) { throw "card $Index '$Name' did not take focus (focused: '$($AE::FocusedElement.Current.Name)')" }
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $m = Mark; $t = Key @() 0x0D
        try { $line = Next $m 'Noto\.Editor rendered chars=' 15000; return [pscustomobject]@{ Inner = Inner $line; Seen = Since $t $line; Mark = $m } }
        catch { if ($attempt -eq 2) { throw "Enter did not open card $Index '$Name' (focused: '$($AE::FocusedElement.Current.Name)')" }; $f = $AE::FocusedElement; try { $f.SetFocus() } catch { }; Start-Sleep -Milliseconds 300 }
    }
}

# A deterministic Markdown document of about $Kb KB: headings, prose, tasks and code, CRLF as Windows writes it.
function Document([int] $Kb, [string] $Seed) {
    $sb = [System.Text.StringBuilder]::new()
    for ($s = 1; $sb.Length -lt $Kb * 1000; $s++) {
        [void]$sb.AppendLine("## $Seed section $s").AppendLine()
        [void]$sb.AppendLine("Discussed the plan for milestone $s with the team: the budget, the migration window and the support load. **Owner** to follow up; see ``notes.md`` and [the brief](https://example.com/$s).").AppendLine()
        foreach ($i in 1..4) { [void]$sb.AppendLine("- [$(if ($i % 2) { ' ' } else { 'x' })] item $s.$i about the budget line") }
        [void]$sb.AppendLine().AppendLine('```').AppendLine("select id from notes where folder_id = $s;").AppendLine('```').AppendLine()
    }
    $sb.ToString()
}
function New-Png([string] $Path, [int] $Width, [int] $Height, [int] $Seed) {
    $b = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($b)
    try {
        $rng = [System.Random]::new($Seed)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new($Width, $Height)), ([System.Drawing.Color]::FromArgb(255, $rng.Next(256), $rng.Next(256), $rng.Next(256))), ([System.Drawing.Color]::FromArgb(255, $rng.Next(256), $rng.Next(256), $rng.Next(256)))
        $g.FillRectangle($brush, 0, 0, $Width, $Height); $brush.Dispose()
        for ($i = 0; $i -lt 400; $i++) { $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, $rng.Next(256), $rng.Next(256), $rng.Next(256))), 3; $g.DrawEllipse($pen, $rng.Next($Width), $rng.Next($Height), $rng.Next(20, 300), $rng.Next(20, 300)); $pen.Dispose() }
    }
    finally { $g.Dispose() }
    $b.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
function Seed([string] $Data, [switch] $Large) {
    Assert-UnderRun $run $Data
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $Data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$(Join-Path $Data 'noto.db');Pooling=False"); $c.Open()
    $tx = $c.BeginTransaction(); $now = [DateTimeOffset]::UtcNow.ToString('O')
    $exec = { param($sql, $p) $cmd = $c.CreateCommand(); $cmd.Transaction = $tx; $cmd.CommandText = $sql; foreach ($k in $p.Keys) { [void]$cmd.Parameters.AddWithValue($k, $p[$k]) }; [void]$cmd.ExecuteNonQuery() }
    $folder = { param($id, $name, $order, $pinned, $deleted) & $exec 'INSERT INTO Folders (Id, Name, SortOrder, IsPinned, CreatedAt, UpdatedAt, DeletedAt) VALUES ($i, $n, $o, $p, $t, $t, $d)' @{ '$i' = $id; '$n' = $name; '$o' = [double]$order; '$p' = [int]$pinned; '$t' = $now; '$d' = $(if ($deleted) { $now } else { [DBNull]::Value }) } }
    $note = { param($id, $f, $content, $order, $pinned, $folded, $deleted) & $exec 'INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsPinned, IsFolded, CreatedAt, UpdatedAt, DeletedAt) VALUES ($i, $f, $c, $o, $p, $d2, $t, $t, $d)' @{ '$i' = $id; '$f' = $f; '$c' = $content; '$o' = [double]$order; '$p' = [int]$pinned; '$d2' = [int]$folded; '$t' = $now; '$d' = $(if ($deleted) { $now } else { [DBNull]::Value }) } }
    $id = { param($prefix, $n) $prefix + $n.ToString('D' + (26 - $prefix.Length)) }
    try {
        $rng = [System.Random]::new(20261105)
        $words = 'meeting', 'budget', 'roadmap', 'invoice', 'travel', 'groceries', 'design', 'review', 'hiring', 'launch'
        $text = { param($n)
            $w = $words[$n % $words.Count]; $roll = $rng.Next(100)
            if ($roll -lt 70) { "$w $n`n`nA short note about the ${w}: call back, check the numbers.`n- [ ] follow up" }
            elseif ($roll -lt 95) { "# $w $n`n`n" + ("The ${w} for this week: what was agreed, who owns it and what is next. " * 12) + "`n- [ ] one`n- [x] two`n" }
            else { "# $w $n long`n`n" + (("Paragraph about the $w with **bold**, *italics* and ``code``. " * 20) + "`n`n") * 8 }
        }
        $total = if ($Large) { $Notes } else { 50 }; $folderCount = if ($Large) { $Folders } else { 5 }
        for ($f = 1; $f -le $folderCount; $f++) { & $folder (& $id '01J0000000F' $f) ("Folder {0:000}" -f $f) $f ($f % 10 -eq 0) $false }
        for ($n = 1; $n -le $total; $n++) { & $note (& $id '01J0000000N' $n) (& $id '01J0000000F' (1 + ($n % $folderCount))) (& $text $n) $n ($n % 20 -eq 0) ($n % 23 -eq 0) $false }
        if ($Large) {
            & $folder '01J0000000000000000000B1G1' 'Big' ($folderCount + 1) $false $false
            for ($n = 1; $n -le 500; $n++) { & $note (& $id '01J0000000B' $n) '01J0000000000000000000B1G1' (& $text ($n * 7)) $n $false $false $false }
            & $folder '01J0000000000000000000S1ZE' 'Sizes' ($folderCount + 2) $false $false
            $o = 0; foreach ($kb in 5, 20, 50, 100, 250) { $o++; & $note (& $id '01J0000000Z' $kb) '01J0000000000000000000S1ZE' (Document $kb "Size $kb KB") $o $false $false $false }
            & $folder '01J0000000000000000000ZBRA' 'Zebra' ($folderCount + 3) $false $false
            for ($n = 1; $n -le 5; $n++) { & $note (& $id '01J0000000R' $n) '01J0000000000000000000ZBRA' "zebra$(7000 + $n) is the rare word in this note" $n $false $false $false }
            & $folder '01J0000000000000000000B1NF' 'Binned folder' ($folderCount + 4) $false $true
            for ($n = 1; $n -le 200; $n++) { & $note (& $id '01J0000000D' $n) $(if ($n -le 20) { '01J0000000000000000000B1NF' } else { (& $id '01J0000000F' (1 + ($n % $folderCount))) }) (& $text ($n * 3)) (100000 + $n) $false $false $true }
            # Pictures: real PNG files, Noto-named, referred to from one note.
            $att = Join-Path $Data 'attachments'; New-Item -ItemType Directory $att -Force | Out-Null
            $refs = for ($p = 1; $p -le $Pictures; $p++) {
                $name = (& $id '01J0000000P' $p) + '.png'
                if ($p -eq 1) { New-Png (Join-Path $att $name) 4000 3000 $p } else { New-Png (Join-Path $att $name) 1600 1200 $p }
                "![Picture $p](attachments/$name)"
            }
            & $folder '01J0000000000000000000P1CS' 'Pictures' ($folderCount + 5) $false $false
            & $note '01J0000000000000000000P1C1' '01J0000000000000000000P1CS' ("Photos`n`n" + ($refs -join "`n`n")) 1 $false $false $false
            & $note '01J0000000000000000000P1C2' '01J0000000000000000000P1CS' "Paste and drop here`n`nlast line" 2 $false $false $false
        }
        $tx.Commit()
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function Start-On([string] $Data, [string] $Exe = $NotoExe) {
    $p = Start-ViaShell $run -File $Exe -Arguments "--data-root=`"$Data`"" -Marker "--data-root=`"$Data`""
    $created = (Get-Process -Id $p.Pid).StartTime
    $script:noto = [pscustomobject]@{ Pid = $p.Pid; Hwnd = [IntPtr]::Zero; Created = $created }
    $line = Next 0 'Noto\.Folders shown folders='
    $null = Until { $script:noto.Hwnd = [NotoVal.Win32]::TopWindows([uint32]$p.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' -and [NotoVal.Win32]::IsWindowVisible($_) } | Select-Object -First 1; $script:noto.Hwnd } 30000
    $hotkey = Next 0 'Noto\.Hotkey registered'
    [pscustomobject]@{ FoldersShown = Since $created $line; FoldersInner = Inner $line; Hotkey = Since $created $hotkey }
}
function Stop-Owner {
    if ($script:noto -and (Get-Process -Id $script:noto.Pid -ErrorAction SilentlyContinue)) {
        $t = Get-Date
        if (-not (Stop-Noto ([pscustomobject]@{ Pid = $script:noto.Pid; Hwnd = $script:noto.Hwnd }))) { throw 'Noto did not exit cleanly' }
        $script:closes += ((Get-Date) - $t).TotalMilliseconds
    }
    $script:noto = $null
}
function Invoke-Sta([string] $Name, [string] $Body, [string[]] $Arguments) {
    $file = Join-Path $run.Root "$Name.ps1"; Assert-UnderRun $run $file
    Set-Content $file $Body
    & powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File $file @Arguments
    Start-Sleep -Milliseconds 500   # a clipboard just set by another process settles first (Invoke-Images.ps1)
}
$SetImage = 'Add-Type -AssemblyName System.Windows.Forms, System.Drawing; $b = New-Object System.Drawing.Bitmap ([int]$args[0]), ([int]$args[1]); $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear([System.Drawing.Color]::SteelBlue); $g.Dispose(); $d = New-Object System.Windows.Forms.DataObject; $d.SetImage($b); [System.Windows.Forms.Clipboard]::SetDataObject($d, $true, 10, 100); if (-not [System.Windows.Forms.Clipboard]::ContainsImage()) { throw "the clipboard did not take the picture" }'

