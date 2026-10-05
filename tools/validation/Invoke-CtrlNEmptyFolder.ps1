#Requires -Version 7.4
<#
.SYNOPSIS
  Ctrl+N creates a note in an empty folder, once per press, on the note surface only.

.DESCRIPTION
  A fresh isolated data root seeded with one empty folder. Keys reach Noto only while it is verifiably the
  foreground window (the activation hotkey arranges that); nothing is typed into the editor.

    1  empty folder: where focus is, then Ctrl+N -> one note created, editor open with focus in it
    2  back in the list (one row now), Ctrl+N    -> exactly one more note, not two
    3  Ctrl+N inside the editor                  -> no note created (unbound there, parity B1)
    4  Ctrl+N on the folder list                 -> still the folder name box (C2), no note created

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-CtrlNEmptyFolder.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'ctrl-n-empty-folder'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F069'

function Sql($Data, [string] $Text, [hashtable] $Parameters = @{}, [switch] $Scalar) {
    $db = Join-Path $Data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Text
        foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }
        if ($Scalar) { $cmd.ExecuteScalar() } else { $cmd.ExecuteNonQuery() }
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function NoteCount($Data) { [int](Sql $Data 'SELECT COUNT(*) FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL' @{ '$f' = $FolderId } -Scalar) }

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
function Items($Noto, [string] $ListId) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    @((Find-ById $Noto $ListId).FindAll($Scope::Descendants, $cond))
}
function Focused { $f = $AE::FocusedElement; "$($f.Current.ControlType.ProgrammaticName)|id=$($f.Current.AutomationId)|name=$($f.Current.Name)" }
function Send-ToNoto($Noto, [int[]] $Mods, [int] $Key) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $Noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 900
}

exit (Invoke-IsolatedRun $run -Title 'Ctrl+N in an empty folder (#69)' -Body {
    Assert-ChordFree $run
    $target = Start-TargetApp $run
    $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql $data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Empty folder', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }

    $noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
    $null = Set-ForegroundByKeyboard $target
    Send-ActivationChord -AllowedForeground $target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }

    # Enter the empty folder.
    $folder = (Items $noto 'FolderList')[0]
    $folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $folder.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto $noto @(0x11) 0x28
    $null = Find-ById $noto 'BackButton'
    $emptyList = (Items $noto 'NoteList').Count
    $focusEmpty = Focused
    Add-Result $run '1  the folder is empty and its note surface is shown' ($emptyList -eq 0 -and (NoteCount $data) -eq 0) "rows=$emptyList focus=$focusEmpty"

    # 1 - Ctrl+N in the empty folder
    Send-ToNoto $noto @(0x11) 0x4E
    $editorShown = $true; try { $null = Find-ById $noto 'NoteEditor' 5 } catch { $editorShown = $false }
    $focusAfter = Focused
    Add-Result $run '1  Ctrl+N in an empty folder creates exactly one note' ((NoteCount $data) -eq 1) "notes=$(NoteCount $data)"
    Add-Result $run '1  the new note opens in the editor, with focus in it' ($editorShown -and $focusAfter -match 'id=NoteEditor') "focus=$focusAfter"

    # 3 - Ctrl+N inside the editor: unbound
    Send-ToNoto $noto @(0x11) 0x4E
    Add-Result $run '3  Ctrl+N inside the editor creates nothing' ((NoteCount $data) -eq 1) "notes=$(NoteCount $data)"

    # 2 - back in the list with one row, Ctrl+N once -> exactly one more
    Send-ToNoto $noto @() 0x1B
    $null = Find-ById $noto 'BackButton'
    $rows = Items $noto 'NoteList'
    if ($rows.Count -gt 0) { $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[0].SetFocus(); Start-Sleep -Milliseconds 300 }
    $focusList = Focused
    Send-ToNoto $noto @(0x11) 0x4E
    Add-Result $run '2  Ctrl+N with a row focused creates exactly one more note (no double handling)' ((NoteCount $data) -eq 2) "rows before=$($rows.Count) focus=$focusList notes=$(NoteCount $data)"

    # 4 - Ctrl+N on the folder list still means "new folder"
    Send-ToNoto $noto @() 0x1B                                   # leave the editor case 2 opened
    $null = Find-ById $noto 'BackButton'
    Send-ToNoto $noto @() 0x1B                                   # leave the folder
    $null = Find-ById $noto 'FolderList'
    $f2 = (Items $noto 'FolderList')[0]; $f2.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto $noto @(0x11) 0x4E
    $focusFolders = Focused
    Add-Result $run '4  Ctrl+N on the folder list focuses the folder name box and creates no note' ($focusFolders -match 'id=FolderNameInput' -and (NoteCount $data) -eq 2) "focus=$focusFolders notes=$(NoteCount $data)"

    Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $noto) ''
})
