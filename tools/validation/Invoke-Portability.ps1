#Requires -Version 7.4
<#
.SYNOPSIS
  Data portability (ADR-020): Back up, Restore, Export and Import from Settings and a folder's menu, end to end through
  Windows' own file dialogs, on a realistic workspace, with timings: every result checked in the files and the database.

.DESCRIPTION
  A fresh isolated data root seeded with 20 folders, 2,000 notes (Unicode, CRLF, a long one) and 150 one-megabyte
  pictures the notes refer to. Every file the run writes is under the run's own directory. The file dialogs are
  Windows' own, owned by Noto's window (Windows hosts them in a process of its own whose controls offer UI Automation
  no patterns): found by ownership, their name box given its text (WM_SETTEXT) and their button its click (BM_CLICK),
  never by keys. Keys reach Noto only while it is verifiably the foreground window. A restore restarts Noto:
  the new process is found by its data root and stopped at the end like the first.

     1  Settings has Back up, Restore, Export and Import, each named, and a named progress bar
     2  Back up writes one archive: its manifest counts the workspace and lists the database and every picture
     3  Export writes every note as Markdown, byte for byte, in a directory per folder, with the index
     4  Import of that export adds every folder (the empty one too) and note beside the existing ones, names numbered
     5  a damaged backup is refused as damaged; nothing changes and nothing is staged
     6  a backup from a newer Noto is refused as such; nothing changes
     7  a file that is not a backup is refused as such; nothing changes
     8  Restore asks first, Cancel focused; cancelling restores nothing and stages nothing
     9  a backup cancelled while it runs leaves no file
    10  Restore replaces everything and restarts: the import is gone, the restart says so, and what was there is backed up
    11  a folder exports from its menu; a folder with no notes says there is nothing to export
    12  timings: backup, export, import and restore on this workspace
    13  a picture that is missing: the export says so, and importing that export says it was not brought in
    14  a restore interrupted at start (a staged picture held open) rolls back: Noto says so and the workspace is
        exactly what it was

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Portability.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe, [int] $Notes = 2000, [int] $Folders = 20, [int] $Pictures = 150, [int] $PictureBytes = 1048576)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
if (-not ('NotoVal.DialogText' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace NotoVal {
    // The file dialog Windows hosts for Noto, in a process of its own whose controls offer UI Automation no patterns:
    // its name box is given its text, and its button its click, by the controls' own messages.
    public static class DialogText {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        public static void Set(IntPtr edit, string text) { SendMessage(edit, 0x000C, IntPtr.Zero, text); }          // WM_SETTEXT
        // Posted, never sent: a sent click runs the dialog's Save inside the sender's call, where its own COM call is
        // refused ("an outgoing call cannot be made ... dispatching an input-synchronous call", measured).
        public static void Click(IntPtr button) { PostMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); }           // BM_CLICK
    }
}
'@
}
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'portability'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$Invoke = [System.Windows.Automation.InvokePattern]::Pattern
$Value = [System.Windows.Automation.ValuePattern]::Pattern

function Sql($Data, [string] $Text, [hashtable] $Parameters = @{}, [switch] $Scalar) {
    $db = Join-Path $Data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db;Pooling=False"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Text
        foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }
        if ($Scalar) { $cmd.ExecuteScalar() } else { [void]$cmd.ExecuteNonQuery() }
    }
    finally { $c.Close() }
}
function Count([string] $Sql) { [int](Sql $script:data $Sql -Scalar) }
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Text-Of([string] $Id) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { $e.Current.Name } else { '' } }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 600) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Bring-Forward {
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $null = Set-ForegroundByKeyboard $script:target
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:target.Hwnd) { Send-ActivationChord -AllowedForeground $script:target.Hwnd }
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:noto.Hwnd) { return }
        Start-Sleep -Milliseconds 1000
    }
    throw 'the hotkey did not bring Noto forward'
}
function Press([string] $Id) { (Find-ById $Id).GetCurrentPattern($Invoke).Invoke() }
function Open-Settings {
    Send-ToNoto @(0x11) 0xBC 1200; $null = Find-ById 'SettingsRoot'
    # The Data group is the page's last: scrolled to, so its rows are on screen.
    (Find-ById 'SettingsScroll').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100)
    Start-Sleep -Milliseconds 600
}

function Answer-Dialog([string] $Path, [int] $Seconds = 15) {
    <#
      Windows' file dialog, opened by Noto and owned by its window: the path given to its name box and its own default
      button clicked, by the controls' messages. No keys.
    #>
    $deadline = (Get-Date).AddSeconds($Seconds); $dialog = $null
    while (-not $dialog -and (Get-Date) -lt $deadline) {
        # Windows hosts the picker in a process of its own (measured): the dialog is Noto's by ownership, its root owner
        # being Noto's window, never by being any dialog on screen.
        $dialog = @($AE::RootElement.FindAll($Scope::Children, [System.Windows.Automation.PropertyCondition]::new($AE::ClassNameProperty, '#32770')) |
                Where-Object { [NotoVal.Win32]::GetAncestor([IntPtr]$_.Current.NativeWindowHandle, 3) -eq $script:noto.Hwnd })[0]
        if (-not $dialog) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $dialog) {
        $seen = @($AE::RootElement.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { "$($_.Current.Name)[$($_.Current.ClassName)/pid $($_.Current.ProcessId)]" }) -join '; '
        throw "no file dialog opened (Noto pid $($script:noto.Pid); notice='$(Text-Of 'SettingsNoticeText')'; windows: $seen)"
    }
    # The name box: inside the dialog's file name host (Save, Open), or the folder box (Select Folder). Never the
    # address bar, which has the same id; another process's controls report as panes, so by id and value pattern.
    $host_ = $dialog.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'FileNameControlHost'))
    $scope_ = if ($host_) { $host_ } else { $dialog }
    $box = @($scope_.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.AutomationId -in '1001', '1148', '1152' -and $_.Current.Name -notlike 'Address*' })[0]
    if (-not $box) {
        $edits = @($dialog.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.AutomationId -match 'FileName|^1[0-9]{3}$|^1$' -or $_.Current.Name -match 'File name|Folder' } | ForEach-Object { "$($_.Current.ControlType.ProgrammaticName)/'$($_.Current.AutomationId)'/'$($_.Current.Name)'" }) -join '; '
        throw "the dialog '$($dialog.Current.Name)' has no name box (edits: $edits)"
    }
    [NotoVal.DialogText]::Set([IntPtr]$box.Current.NativeWindowHandle, $Path); Start-Sleep -Milliseconds 300

    # The dialog's own default button: id 1, named for what it does. A file in the list can have id 1 too, so never by id alone.
    $button = @($dialog.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, '1')) |
            Where-Object { $_.Current.ControlType -in $CT::Button, $CT::Pane -and $_.Current.Name -in 'Save', 'Open', 'Select Folder' })[0]
    if (-not $button) { throw "the dialog '$($dialog.Current.Name)' has no Save, Open or Select Folder button" }
    $name = $dialog.Current.Name
    [NotoVal.DialogText]::Click([IntPtr]$button.Current.NativeWindowHandle)
    $name
}
function Wait-Result([int] $Seconds = 180) {
    <#
      The Data line's result and the notice, once the work is over: the rows are off from the moment it starts (before
      its file dialog) until it ends, so a result read while they are off would be the previous one.
    #>
    $deadline = (Get-Date).AddSeconds($Seconds); $since = Get-Date
    while ((Get-Date) -lt $deadline) {
        # A dialog of Noto's still open long after it was answered is an error box over it: stop and say what it says,
        # never wait for someone to press OK (review: a sent click raised one, and the run waited on it).
        $owned = @($AE::RootElement.FindAll($Scope::Children, [System.Windows.Automation.PropertyCondition]::new($AE::ClassNameProperty, '#32770')) |
                Where-Object { [NotoVal.Win32]::GetAncestor([IntPtr]$_.Current.NativeWindowHandle, 3) -eq $script:noto.Hwnd })
        if ($owned.Count -gt 0 -and ((Get-Date) - $since).TotalSeconds -gt 8) {
            $text = @($owned | ForEach-Object { $_.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Text)) } | ForEach-Object { $_.Current.Name }) -join ' / '
            throw "a dialog of Noto's stayed open: '$($owned[0].Current.Name)': $text"
        }
        if ((By-Id 'BackupButton') -and (By-Id 'BackupButton').Current.IsEnabled) {
            Start-Sleep -Milliseconds 200
            # Read wherever they are: the page may have scrolled them out of view.
            $status = By-Id 'DataStatusText'; $notice = By-Id 'SettingsNoticeText'
            return [pscustomobject]@{ Status = if ($status) { $status.Current.Name } else { '' }; Notice = if ($notice) { $notice.Current.Name } else { '' } }
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'the Data work did not finish'
}
function Snapshot { "folders=$(Count 'SELECT COUNT(*) FROM Folders WHERE DeletedAt IS NULL') notes=$(Count 'SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NULL')" }
function Manifest([string] $Archive) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Archive)
    try { $r = New-Object System.IO.StreamReader($zip.GetEntry('manifest.json').Open()); try { $r.ReadToEnd() | ConvertFrom-Json } finally { $r.Dispose() } } finally { $zip.Dispose() }
}
function Rewrite-Archive([string] $From, [string] $To, [scriptblock] $Change) {
    <# A copy of a backup with each entry's bytes as $Change returns them. #>
    $in = [System.IO.Compression.ZipFile]::OpenRead($From); $out = [System.IO.Compression.ZipFile]::Open($To, 'Create')
    try {
        foreach ($entry in $in.Entries) {
            $ms = New-Object System.IO.MemoryStream; $s = $entry.Open(); $s.CopyTo($ms); $s.Dispose()
            [byte[]] $bytes = & $Change $entry.FullName $ms.ToArray()
            $o = $out.CreateEntry($entry.FullName).Open(); $o.Write($bytes, 0, $bytes.Length); $o.Dispose()
        }
    }
    finally { $in.Dispose(); $out.Dispose() }
}
function Find-Restarted([int] $OldPid, [int] $Seconds = 60) {
    <# The Noto that a restore started: the same executable on this run's data root, a new process, with its window. #>
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $p = @(Get-CimInstance Win32_Process -Filter "Name='Noto.exe'" | Where-Object { $_.ProcessId -ne $OldPid -and $_.CommandLine -like "*$($script:data)*" })[0]
        if ($p) {
            $h = [NotoVal.Win32]::TopWindows([uint32]$p.ProcessId) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' -and [NotoVal.Win32]::IsWindowVisible($_) } | Select-Object -First 1
            if ($h) {
                Add-Started $run $p.ProcessId ((Get-Process -Id $p.ProcessId).StartTime.ToUniversalTime().Ticks)
                Start-Sleep -Seconds 2
                return [pscustomobject]@{ Pid = $p.ProcessId; Hwnd = $h; DataRoot = $script:data }
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'Noto did not start again after the restore'
}

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'Data portability (ADR-020)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $out = Join-Path $run.Root 'out'; New-Item -ItemType Directory $out | Out-Null

    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }

    # The workspace: folders, notes of every kind, pictures the notes refer to.
    $attachments = Join-Path $script:data 'attachments'; New-Item -ItemType Directory $attachments -Force | Out-Null
    $alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'
    $rng = [System.Random]::new(20261104)
    $ulid = { param($n, $prefix) $prefix + (-join (1..(26 - $prefix.Length) | ForEach-Object { $alphabet[($n -shr (5 * ($_ % 6))) % 32] })) }
    $pictureRefs = @(for ($i = 0; $i -lt $Pictures; $i++) {
            $name = (& $ulid ($i * 7919 + 13) '01J0000000P') + '.png'
            $bytes = [byte[]]::new($PictureBytes); $rng.NextBytes($bytes)
            [byte[]] $sig = 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A; [Array]::Copy($sig, $bytes, 8)
            [System.IO.File]::WriteAllBytes((Join-Path $attachments $name), $bytes)
            "attachments/$name"
        })
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$(Join-Path $script:data 'noto.db');Pooling=False"); $c.Open()
    try {
        $tx = $c.BeginTransaction(); $now = [DateTimeOffset]::UtcNow.ToString('O')
        $folderIds = @(for ($f = 0; $f -lt $Folders; $f++) {
                $id = & $ulid ($f * 104729 + 7) '01J0000000F'
                $cmd = $c.CreateCommand(); $cmd.Transaction = $tx
                $cmd.CommandText = 'INSERT INTO Folders (Id, Name, SortOrder, IsPinned, CreatedAt, UpdatedAt) VALUES ($i, $n, $s, $p, $t, $t)'
                [void]$cmd.Parameters.AddWithValue('$i', $id); [void]$cmd.Parameters.AddWithValue('$n', "Folder $($f.ToString('00')) ✓"); [void]$cmd.Parameters.AddWithValue('$s', [double]$f); [void]$cmd.Parameters.AddWithValue('$p', [int]($f -eq 3)); [void]$cmd.Parameters.AddWithValue('$t', $now)
                [void]$cmd.ExecuteNonQuery(); $id
            })
        $empty = & $ulid 999983 '01J0000000E'
        $cmd = $c.CreateCommand(); $cmd.Transaction = $tx
        $cmd.CommandText = 'INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ($i, ''Nothing here'', 99, $t, $t)'
        [void]$cmd.Parameters.AddWithValue('$i', $empty); [void]$cmd.Parameters.AddWithValue('$t', $now); [void]$cmd.ExecuteNonQuery()
        for ($n = 0; $n -lt $Notes; $n++) {
            $content = switch ($n % 5) {
                0 { "# Meeting $n`r`n`r`n- [ ] follow up`r`n- [x] notes sent`r`n" }
                1 { "Trip $n`n![photo]($($pictureRefs[$n % $Pictures]))`nÜberraschung 🎉 日本語" }
                2 { "Plan $n`n" + ('lorem ipsum dolor sit amet ' * 40) }
                3 { "**Idea** $n`nSee https://example.com/$n and ``code``." }
                4 { "Note $n" }
            }
            if ($n -eq 7) { $content = "Long note`n" + ('x' * 2000000) }
            $cmd = $c.CreateCommand(); $cmd.Transaction = $tx
            $cmd.CommandText = 'INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsPinned, IsFolded, CreatedAt, UpdatedAt) VALUES ($i, $f, $c, $s, $p, $d, $t, $t)'
            [void]$cmd.Parameters.AddWithValue('$i', (& $ulid ($n * 15485863 + 3) '01J0000000N')); [void]$cmd.Parameters.AddWithValue('$f', $folderIds[$n % $Folders]); [void]$cmd.Parameters.AddWithValue('$c', $content)
            [void]$cmd.Parameters.AddWithValue('$s', [double]$n); [void]$cmd.Parameters.AddWithValue('$p', [int]($n % 97 -eq 0)); [void]$cmd.Parameters.AddWithValue('$d', [int]($n % 89 -eq 0)); [void]$cmd.Parameters.AddWithValue('$t', $now)
            [void]$cmd.ExecuteNonQuery()
        }
        $tx.Commit()
    }
    finally { $c.Close() }
    $seeded = Snapshot

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward
    Open-Settings

    # 1 - the rows
    $named = @('BackupButton', 'RestoreButton', 'ExportButton', 'ImportButton' | ForEach-Object { (Find-ById $_).Current.Name })
    Add-Result $run '1  Settings has Back up, Restore, Export and Import, each named' (@($named | Where-Object { -not $_ }).Count -eq 0) "names='$($named -join ' | ')'"

    # 2 - back up
    $backup = Join-Path $out 'everything.notobackup'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Press 'BackupButton'; $dialog = Answer-Dialog $backup
    $result = Wait-Result; $backupMs = $clock.ElapsedMilliseconds
    $m = if (Test-Path $backup) { Manifest $backup } else { $null }
    $listed = if ($m) { @($m.entries).Count } else { 0 }
    Add-Result $run '2  Back up writes one archive whose manifest counts the workspace and lists the database and every picture' ($m -and $m.format -eq 'noto-backup' -and $m.counts.notes -eq $Notes -and $m.counts.folders -eq $Folders + 1 -and $m.counts.images -eq $Pictures -and $listed -eq $Pictures + 1 -and $result.Status -like 'Backed up*') "dialog='$dialog' status='$($result.Status)' entries=$listed size=$([math]::Round((Get-Item $backup).Length / 1MB, 1)) MB in $backupMs ms"

    # 3 - export
    $exports = Join-Path $out 'exports'; New-Item -ItemType Directory $exports | Out-Null
    $clock.Restart()
    Press 'ExportButton'; $null = Answer-Dialog $exports
    $result = Wait-Result; $exportMs = $clock.ElapsedMilliseconds
    $exported = @(Get-ChildItem $exports -Directory)[0]
    $md = @(Get-ChildItem $exported.FullName -Recurse -Filter *.md)
    $sample = Join-Path $exported.FullName "Folder 01 ✓\Trip 1.md"
    $same = (Test-Path -LiteralPath $sample) -and [Convert]::ToHexString([IO.File]::ReadAllBytes($sample)) -eq [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes("Trip 1`n![photo]($($pictureRefs[1]))`nÜberraschung 🎉 日本語"))
    $crlf = Join-Path $exported.FullName "Folder 00 ✓\Meeting 0.md"
    $sameCrlf = (Test-Path -LiteralPath $crlf) -and [IO.File]::ReadAllText($crlf) -eq "# Meeting 0`r`n`r`n- [ ] follow up`r`n- [x] notes sent`r`n"
    Add-Result $run '3  Export writes every note as Markdown, byte for byte, a directory per folder, with the index' ($md.Count -eq $Notes -and $same -and $sameCrlf -and (Test-Path (Join-Path $exported.FullName 'noto-export.json')) -and $result.Status -like 'Exported*') "status='$($result.Status)' files=$($md.Count) unicode same=$same crlf same=$sameCrlf in $exportMs ms"

    # 4 - import that export
    $clock.Restart()
    Press 'ImportButton'; $null = Answer-Dialog $exported.FullName
    $result = Wait-Result; $importMs = $clock.ElapsedMilliseconds
    $numbered = Count "SELECT COUNT(*) FROM Folders WHERE DeletedAt IS NULL AND Name LIKE '% (2)'"
    # Every folder comes back, the empty one too: an export of everything keeps the workspace's folders.
    Add-Result $run '4  Import adds every exported folder and note beside the existing ones, names numbered' ((Snapshot) -eq "folders=$(2 * ($Folders + 1)) notes=$(2 * $Notes)" -and $numbered -eq $Folders + 1 -and $result.Status -like 'Imported*') "status='$($result.Status)' $(Snapshot) numbered=$numbered in $importMs ms"
    $afterImport = Snapshot

    # 5, 6, 7 - refused backups
    $damaged = Join-Path $out 'damaged.notobackup'
    Rewrite-Archive $backup $damaged { param($name, $bytes) if ($name -eq 'noto.db') { $bytes[$bytes.Length / 2] = $bytes[$bytes.Length / 2] -bxor 0xFF }; , $bytes }
    $newer = Join-Path $out 'newer.notobackup'
    Rewrite-Archive $backup $newer { param($name, $bytes) if ($name -eq 'manifest.json') { , [Text.Encoding]::UTF8.GetBytes((([Text.Encoding]::UTF8.GetString($bytes)) -replace '"formatVersion": 1', '"formatVersion": 2')) } else { , $bytes } }
    $notBackup = Join-Path $out 'notes.notobackup'; Set-Content $notBackup 'just some text'
    $case = 5
    foreach ($bad in @(@($damaged, 'damaged'), @($newer, 'newer version'), @($notBackup, "isn't a Noto backup"))) {
        Press 'RestoreButton'; $null = Answer-Dialog $bad[0]
        $result = Wait-Result
        $staged = Test-Path (Join-Path $script:data 'restore-staging')
        Add-Result $run "$case  $(Split-Path $bad[0] -Leaf) is refused ($($bad[1])); nothing changes and nothing is staged" ($result.Notice -like "*$($bad[1])*" -and (Snapshot) -eq $afterImport -and -not $staged) "notice='$($result.Notice)' $(Snapshot) staged=$staged"
        $case++
    }

    # 8 - restore asks first, Cancel focused
    Press 'RestoreButton'; $null = Answer-Dialog $backup
    $deadline = (Get-Date).AddSeconds(60); $confirm = $null
    while (-not $confirm -and (Get-Date) -lt $deadline) { $confirm = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Restore and restart')); if (-not $confirm) { Start-Sleep -Milliseconds 300 } }
    $focus = $AE::FocusedElement.Current.Name
    $cancelButton = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Cancel'), [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Button)))
    $cancelButton.GetCurrentPattern($Invoke).Invoke()
    $result = Wait-Result
    $staged = Test-Path (Join-Path $script:data 'restore-staging')
    Add-Result $run '8  Restore asks first, Cancel focused; cancelling restores nothing and stages nothing' ($null -ne $confirm -and $focus -eq 'Cancel' -and $result.Status -eq 'Nothing was restored.' -and (Snapshot) -eq $afterImport -and -not $staged) "dialog=$($null -ne $confirm) focus='$focus' status='$($result.Status)' staged=$staged"

    # 9 - cancel a running backup
    $cancelled = Join-Path $out 'cancelled.notobackup'
    Press 'BackupButton'; $null = Answer-Dialog $cancelled
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) { $b = By-Id 'DataCancelButton'; if ($b -and -not $b.Current.IsOffscreen) { $b.GetCurrentPattern($Invoke).Invoke(); break }; Start-Sleep -Milliseconds 20 }
    $result = Wait-Result
    $left = @(Get-ChildItem $out -Filter 'cancelled*').Count
    Add-Result $run '9  a backup cancelled while it runs leaves no file' ($result.Status -eq 'Cancelled. Nothing was changed.' -and $left -eq 0) "status='$($result.Status)' files left=$left"

    # 10 - restore and restart
    Press 'RestoreButton'; $null = Answer-Dialog $backup
    $deadline = (Get-Date).AddSeconds(60); $confirm = $null
    while (-not $confirm -and (Get-Date) -lt $deadline) { $confirm = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Restore and restart')); if (-not $confirm) { Start-Sleep -Milliseconds 300 } }
    $old = $script:noto.Pid
    $clock.Restart()
    $confirm.GetCurrentPattern($Invoke).Invoke()
    $ended = (Get-Process -Id $old -ErrorAction SilentlyContinue).WaitForExit(120000)
    $script:noto = Find-Restarted $old
    $restoreMs = $clock.ElapsedMilliseconds
    $said = Text-Of 'StatusText'
    $safety = @(Get-ChildItem (Join-Path $script:data 'backups') -Filter 'before-restore-*.notobackup')
    Add-Result $run '10 Restore replaces everything and restarts: the import is gone, the restart says so, and what was there is backed up' ($ended -and (Snapshot) -eq $seeded -and $said -eq 'Your notes were restored from the backup.' -and $safety.Count -eq 1 -and -not (Test-Path (Join-Path $script:data 'restore-staging'))) "ended=$ended $(Snapshot) (seeded $seeded) said='$said' safety=$($safety.Count) confirm to new window $restoreMs ms"

    # 11 - export from a folder's menu; nothing to export
    Bring-Forward
    $rows = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))
    $menuExports = Join-Path $out 'menu'; New-Item -ItemType Directory $menuExports | Out-Null
    $results = foreach ($name in @('Folder 03 ✓', 'Nothing here')) {
        $row = @($rows | Where-Object { $_.Current.Name -eq $name })[0]
        $row.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
        $row.SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @(0x10) 0x79 900
        $item = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Export as Markdown…'))
        if (-not $item) { $item = $AE::RootElement.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Export as Markdown…')) }
        $item.GetCurrentPattern($Invoke).Invoke()
        $null = Answer-Dialog $menuExports
        $expected = if ($name -eq 'Nothing here') { "There's nothing to export*" } else { 'Exported*' }
        $deadline = (Get-Date).AddSeconds(30); $said = ''
        while ((Get-Date) -lt $deadline -and $said -notlike $expected) { $said = Text-Of 'StatusText'; Start-Sleep -Milliseconds 200 }
        $said
    }
    $written = @(Get-ChildItem $menuExports -Directory)
    $folderMd = if ($written.Count -eq 1) { @(Get-ChildItem $written[0].FullName -Recurse -Filter *.md).Count } else { -1 }
    Add-Result $run '11 a folder exports from its menu; a folder with no notes says there is nothing to export' ($results[0] -like 'Exported 100 notes*' -and $results[1] -like "There's nothing to export*" -and $folderMd -eq 100) "said='$($results -join ' | ')' directories=$($written.Count) md=$folderMd"

    # 12 - timings
    Add-Result $run "12 timings on $Notes notes, $Pictures pictures of $([math]::Round($PictureBytes / 1MB, 1)) MB" $true "backup=$backupMs ms export=$exportMs ms import=$importMs ms restore (confirm to new window)=$restoreMs ms"

    # 13 - a missing picture: Folder 01's notes refer to pictures 1, 21, 41...; the first is removed
    Remove-Item (Join-Path $script:data $pictureRefs[1])
    $missingOut = Join-Path $out 'missing'; New-Item -ItemType Directory $missingOut | Out-Null
    $row = @(@((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) | Where-Object { $_.Current.Name -eq 'Folder 01 ✓' })[0]
    $row.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
    $row.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79 900
    $item = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Export as Markdown…'))
    if (-not $item) { $item = $AE::RootElement.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Export as Markdown…')) }
    $item.GetCurrentPattern($Invoke).Invoke()
    $null = Answer-Dialog $missingOut
    $deadline = (Get-Date).AddSeconds(30); $exportSaid = ''
    while ((Get-Date) -lt $deadline -and $exportSaid -notlike 'Exported*') { $exportSaid = Text-Of 'StatusText'; Start-Sleep -Milliseconds 200 }
    Open-Settings
    Press 'ImportButton'; $null = Answer-Dialog (@(Get-ChildItem $missingOut -Directory)[0].FullName)
    $result = Wait-Result
    Add-Result $run '13 a missing picture: the export says so, and importing that export says it was not brought in' ($exportSaid -like '*1 picture couldn''t be found.' -and $result.Status -like 'Imported 100 notes*' -and $result.Notice -like '1 picture couldn''t be brought in*') "export='$exportSaid' import='$($result.Status)' notice='$($result.Notice)'"
    Send-ToNoto @() 0x1B 900

    # 14 - a restore interrupted at start rolls back
    $beforeInterrupted = Snapshot
    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
    $staging = Join-Path $script:data 'restore-staging'
    [System.IO.Compression.ZipFile]::ExtractToDirectory($backup, $staging)
    Remove-Item (Join-Path $staging 'manifest.json')
    Set-Content (Join-Path $script:data 'restore-pending.json') '{}'
    $held = [System.IO.File]::Open((Get-ChildItem (Join-Path $staging 'attachments') -File | Select-Object -First 1).FullName, 'Open', 'Read', 'None')
    try {
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
        Bring-Forward
        $said = Text-Of 'NoticeText'
    }
    finally { $held.Dispose() }
    $after = Snapshot
    Add-Result $run '14 a restore interrupted at start rolls back: Noto says so and the workspace is exactly what it was' ($said -eq 'The backup couldn''t be restored, so your notes are as they were.' -and $after -eq $beforeInterrupted -and -not (Test-Path (Join-Path $script:data 'restore-pending.json'))) "said='$said' before=$beforeInterrupted after=$after marker gone=$(-not (Test-Path (Join-Path $script:data 'restore-pending.json')))"

    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
}) }
finally {
    $script:log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($script:log.All())
}
