#Requires -Version 7.4
<#
.SYNOPSIS
  F9 (#152): files dropped on a folder or on its note stack are imported: each Markdown or text file a new note, each
  picture a new note holding it, first in that folder; one result and one Undo; the files copied, never changed.

.DESCRIPTION
  One isolated data root on a temporary virtual desktop: a folder "Work" with one note and a folder "Home". Each drop is
  a real OLE drag (helpers/drop-files.ps1), the data Explorer and the desktop give, a list of files (CF_HDROP): the
  button is pressed on a small window of the helper's own shown over Noto without taking the foreground, and released
  over the target. Both points are checked to be Noto's window first, and Noto is checked to be still in front after.
  Evidence: the database, the attachments folder, the dropped files' bytes and times, and what UI Automation reads.

    1  on Work's stack: list.md (with a picture beside it), plain.txt, Harbour.png and report.docx: three notes first,
       in that order, the picture note titled by its name; "Imported 3 notes."; the notice names report.docx; Undo is
       offered; two pictures kept; every dropped file unchanged
    2  Undo: the three notes are in the Recycle Bin, "Import undone: 3 notes moved to the Recycle Bin.", no Undo
    3  on the folder list, a file dropped on Home's row: a note in Home, "Imported 1 note into Home.", Undo offered
    4  only report.docx on the stack: "Nothing was imported.", the notice says why, no Undo

  Explorer, the desktop and a browser as the drag's source are the owner's check (#31): a harness cannot drive their
  windows without input outside Noto.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-DropImport.ps1 -NotoExe <frozen Release>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'drop-import'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$Work = '01J0000000000000000000F091'; $HomeFolder = '01J0000000000000000000F092'

function Sql([string] $Text, [hashtable] $Parameters = @{}, [switch] $Scalar) {
    $db = Join-Path $script:data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Text
        foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }
        if ($Scalar) { $cmd.ExecuteScalar() } else { $cmd.ExecuteNonQuery() }
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function Contents([string] $Folder) {
    $db = Join-Path $script:data 'noto.db'; Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT Content FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder, Id'
        [void]$cmd.Parameters.AddWithValue('$f', $Folder)
        $r = $cmd.ExecuteReader(); $out = @(); while ($r.Read()) { $out += $r.GetString(0) }; $r.Close(); , $out
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function Binned { [int](Sql 'SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL' -Scalar) }
function Attachments { @(Get-ChildItem (Join-Path $script:data 'attachments') -File -ErrorAction SilentlyContinue | ForEach-Object Name) }
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Shown-Text([string] $Id) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { $e.Current.Name } else { '' } }
function Visible-Id([string] $Id) { $e = By-Id $Id; [bool]($e -and -not $e.Current.IsOffscreen) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Rows([string] $List) { @((Find-ById $List).FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 700) {
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
function Drop-On($Element, [string[]] $Files) {
    <# A real OLE drag of $Files, from just below the element's centre (the helper's window, over Noto) onto its centre. #>
    $r = $Element.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2); $fromX = $x; $fromY = [int]($y + [Math]::Min(60, $r.Height / 2 - 4))
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to drop: Noto is not the foreground window' }
    foreach ($point in @(@($x, $y), @($fromX, $fromY))) {
        $pt = New-Object NotoVal.Win32+POINT; $pt.X = $point[0]; $pt.Y = $point[1]
        if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $script:noto.Hwnd) { throw "refusing to drag at ($($point[0]),$($point[1])): it is not Noto" }
    }
    $effect = & powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'helpers/drop-files.ps1') "$fromX" "$fromY" "$x" "$y" @Files
    Start-Sleep -Milliseconds 2000
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the drag took the foreground from Noto' }
    "$effect"
}
function Invoke-Button([string] $Id) { (Find-ById $Id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200 }
function New-Png([string] $Path) {
    $b = New-Object System.Drawing.Bitmap 40, 20; $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear([System.Drawing.Color]::Red); $g.Dispose()
    $b.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
function Stamp([string[]] $Files) { ($Files | ForEach-Object { "$((Get-FileHash $_).Hash)@$((Get-Item $_).LastWriteTimeUtc.Ticks)" }) -join '|' }

exit (Invoke-IsolatedRun $run -Title 'F9: drop files to import them' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Work', 1, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }
    $null = Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Home', 2, `$t, `$t)" @{ '$f' = $HomeFolder; '$t' = $now }
    $null = Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES ('01J00000000000000000000D01', `$f, 'Already here', 1, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }

    # The files, outside the data root, as a user's would be.
    $from = Join-Path $run.Root 'dropped'; New-Item -ItemType Directory (Join-Path $from 'pictures') -Force | Out-Null
    $list = Join-Path $from 'list.md'; [IO.File]::WriteAllText($list, "# List`n- milk`n![dot](pictures/dot.png)")
    New-Png (Join-Path $from 'pictures\dot.png')
    $plain = Join-Path $from 'plain.txt'; [IO.File]::WriteAllText($plain, 'Plain text')
    $photo = Join-Path $from 'Harbour.png'; New-Png $photo
    $doc = Join-Path $from 'report.docx'; [IO.File]::WriteAllText($doc, 'not a note')
    $homeFile = Join-Path $from 'home.md'; [IO.File]::WriteAllText($homeFile, 'Home note')
    $all = @($list, $plain, $photo, $doc, $homeFile, (Join-Path $from 'pictures\dot.png'))
    (Get-Item $all) | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]::new(2026, 1, 2, 3, 4, 5, [DateTimeKind]::Utc) }
    $stamped = Stamp $all

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward
    $workRow = @(Rows 'FolderList' | Where-Object { $_.Current.Name -eq 'Work' })[0]
    $workRow.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x28 1500

    # 1 - four files on the stack
    $pictures = @(Attachments).Count
    $effect = Drop-On (Find-ById 'NoteList') @($list, $plain, $photo, $doc)
    $notes = Contents $Work
    $status = Shown-Text 'NoteStatusText'; $notice = Shown-Text 'NoteNoticeText'
    $cards = @(Rows 'NoteList' | ForEach-Object { $_.Current.Name })
    Add-Result $run '1  four files on the stack: three notes first, in order; one result; the .docx named; Undo; files unchanged' (
        $notes.Count -eq 4 -and $notes[0] -match '^# List\n- milk\n!\[dot\]\(attachments/' -and $notes[1] -eq 'Plain text' -and $notes[2] -match '^Harbour\n!\[Harbour\.png\]\(attachments/' -and $notes[3] -eq 'Already here' -and
        $cards[2] -eq 'Harbour' -and $status -eq 'Imported 3 notes.' -and $notice -match 'report\.docx' -and (Visible-Id 'NoteUndoImportButton') -and @(Attachments).Count -eq $pictures + 2 -and (Stamp $all) -eq $stamped
    ) "$effect notes=$($notes.Count) first='$($notes[0] -replace "`n", ' | ')' cards=$($cards -join ',') status='$status' notice='$notice' undo=$(Visible-Id 'NoteUndoImportButton') pictures=+$(@(Attachments).Count - $pictures) unchanged=$((Stamp $all) -eq $stamped)"

    # 2 - Undo
    Invoke-Button 'NoteUndoImportButton'
    $status = Shown-Text 'NoteStatusText'
    Add-Result $run '2  Undo moves the three notes to the Recycle Bin, says so, and is no longer offered' ((Contents $Work).Count -eq 1 -and (Binned) -eq 3 -and $status -eq 'Import undone: 3 notes moved to the Recycle Bin.' -and -not (Visible-Id 'NoteUndoImportButton')) "left=$((Contents $Work) -join ',') binned=$(Binned) status='$status' undo=$(Visible-Id 'NoteUndoImportButton')"

    # 4 - nothing importable
    $effect = Drop-On (Find-ById 'NoteList') @($doc)
    $status = Shown-Text 'NoteStatusText'; $notice = Shown-Text 'NoteNoticeText'
    Add-Result $run '4  only a .docx: nothing imported, the notice says why, no Undo' ((Contents $Work).Count -eq 1 -and $status -eq 'Nothing was imported.' -and $notice -match 'report\.docx wasn''t imported: only Markdown, text files and pictures' -and -not (Visible-Id 'NoteUndoImportButton')) "$effect status='$status' notice='$notice' undo=$(Visible-Id 'NoteUndoImportButton')"

    # 3 - on the folder list, a file dropped on Home's row
    Invoke-Button 'BackButton'
    $homeRow = @(Rows 'FolderList' | Where-Object { $_.Current.Name -eq 'Home' })[0]
    $effect = Drop-On $homeRow @($homeFile)
    $status = Shown-Text 'StatusText'
    Add-Result $run '3  a file dropped on Home''s row: a note in Home, one result, Undo offered; Work unchanged' (((Contents $HomeFolder) -join ',') -eq 'Home note' -and (Contents $Work).Count -eq 1 -and $status -eq 'Imported 1 note into Home.' -and (Visible-Id 'UndoImportButton') -and (Stamp $all) -eq $stamped) "$effect home=$((Contents $HomeFolder) -join ',') status='$status' undo=$(Visible-Id 'UndoImportButton') unchanged=$((Stamp $all) -eq $stamped)"

    $null = Stop-Noto $script:noto
})
