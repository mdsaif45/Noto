#Requires -Version 7.4
<#
.SYNOPSIS
  Deleting folders and the Recycle Bin (Phase B5): confirmations, soft deletes, restore, permanent delete, empty.

.DESCRIPTION
  A fresh isolated data root with folders Work (Plan, Todo), Home (Milk) and Empty. Keys reach Noto only while it is
  verifiably the foreground window; text is set through UI Automation; confirmations are answered through UI
  Automation or by key.

     1  Delete on a folder: a confirmation at the row, Cancel focused; Escape closes it and nothing is written
     2  its Cancel button: nothing is written
     3  a card edited, then Back: saved; the save that fails keeps the stack (no route to a delete), nothing written
     4  the folder menu's Delete on Work (2 notes): the confirmation names them; Delete bins the folder and both notes
        under one timestamp; Home is untouched; focus goes to the next folder; I7 holds
     5  the Recycle Bin opens from its button: Work (Folder, 2 notes) and its notes, help text on each entry
     6  a note whose folder is in the bin: Enter writes nothing (it is announced, B6); Restore is disabled and not in
        its menu; Restore to Home restores it there, with the text typed in case 3
     7  Enter on Work restores the folder with the rest of its notes
     8  Delete permanently on a folder: the dialog's default is Cancel (Escape writes nothing); confirming purges it
     9  Empty: Cancel writes nothing; confirming purges everything; the empty state shows and Empty is disabled
    10  Escape leaves the bin for the folder list, focus back on the Recycle Bin button
    11  the bin's controls are named

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-RecycleBin.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'recycle-bin'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$Work = '01J0000000000000000000FB01'; $Home_ = '01J0000000000000000000FB02'; $Empty = '01J0000000000000000000FB03'
$Plan = '01J00000000000000000000B01'; $Todo = '01J00000000000000000000B02'; $Milk = '01J00000000000000000000B03'

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
function Deleted([string] $Table, [string] $Id) { [string](Sql "SELECT IFNULL(DeletedAt, '') FROM $Table WHERE Id = `$i" @{ '$i' = $Id } -Scalar) }
function Exists([string] $Table, [string] $Id) { [int](Sql "SELECT COUNT(*) FROM $Table WHERE Id = `$i" @{ '$i' = $Id } -Scalar) -eq 1 }
function I7 { [int](Sql 'SELECT COUNT(*) FROM Notes n WHERE n.DeletedAt IS NULL AND NOT EXISTS (SELECT 1 FROM Folders f WHERE f.Id = n.FolderId AND f.DeletedAt IS NULL)' -Scalar) -eq 0 }
function Snapshot { [string](Sql "SELECT GROUP_CONCAT(x, '|') FROM (SELECT Id || IFNULL(DeletedAt, '') || UpdatedAt || Content AS x FROM Notes UNION ALL SELECT Id || IFNULL(DeletedAt, '') || UpdatedAt || Name FROM Folders ORDER BY 1)" -Scalar) }

function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Visible-Id([string] $Id) { $e = By-Id $Id; [bool]($e -and -not $e.Current.IsOffscreen) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Rows([string] $ListId) { @((Find-ById $ListId).FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) }
function Row([string] $ListId, [string] $Name) { Rows $ListId | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1 }
function Focused { $f = $AE::FocusedElement; "$($f.Current.ControlType.ProgrammaticName)|id=$($f.Current.AutomationId)|name=$($f.Current.Name)" }
function Focused-Name { $AE::FocusedElement.Current.Name }
function Mine([System.Windows.Automation.ControlType] $Type, [string] $Name) {
    $cond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $Type),
        [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, $Name))
    @($AE::RootElement.FindAll($Scope::Descendants, $cond) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid -and -not $_.Current.IsOffscreen }) | Select-Object -First 1
}
function Wait-Mine([System.Windows.Automation.ControlType] $Type, [string] $Name, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = Mine $Type $Name; if ($e) { return $e }; Start-Sleep -Milliseconds 200 }
    $null
}
function Press([System.Windows.Automation.AutomationElement] $Button) { $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900 }
function Texts { @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Text)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid -and -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) -join ' / ' }
function Menu-Items { @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::MenuItem)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid } | ForEach-Object { $_.Current.Name }) }
function Send-ToNoto([int[]] $Mods, [int] $Key) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 900
}
function Editor { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'NoteEditor')) }
function Focus-Row([string] $ListId, [string] $Name) { $r = Row $ListId $Name; if (-not $r) { throw "no row '$Name' in $ListId" }; $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $r.SetFocus(); Start-Sleep -Milliseconds 300 }

exit (Invoke-IsolatedRun $run -Title 'Deleting folders and the Recycle Bin (Phase B5)' -Body {
    Assert-ChordFree $run
    $target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $i = 0
    foreach ($f in @(@($Work, 'Work'), @($Home_, 'Home'), @($Empty, 'Empty'))) { $i++; $null = Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, `$n, `$s, `$t, `$t)" @{ '$f' = $f[0]; '$n' = $f[1]; '$s' = $i; '$t' = $now } }
    $i = 0
    foreach ($n in @(@($Plan, $Work, 'Plan'), @($Todo, $Work, 'Todo'), @($Milk, $Home_, 'Milk'))) { $i++; $null = Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" @{ '$n' = $n[0]; '$f' = $n[1]; '$c' = $n[2]; '$s' = $i; '$t' = $now } }

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    $null = Set-ForegroundByKeyboard $target
    Send-ActivationChord -AllowedForeground $target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }

    # 1 - Delete on a folder: confirmation, Cancel focused; Escape writes nothing
    $before = Snapshot
    Focus-Row 'FolderList' 'Empty'
    Send-ToNoto @() 0x2E
    $cancel = Wait-Mine $CT::Button 'Cancel'; $deleteButton = Mine $CT::Button 'Delete folder'
    $cancelFocused = (Focused) -match 'Button\|.*name=Cancel'
    $message = Texts
    Send-ToNoto @() 0x1B
    Add-Result $run '1  Delete on a folder asks first, Cancel focused; Escape closes it and writes nothing' ($cancel -and $deleteButton -and $cancelFocused -and $message -match 'Empty.*0 notes.*Recycle Bin' -and (Snapshot) -eq $before -and -not (Mine $CT::Button 'Delete folder') -and (Focused-Name) -eq 'Empty') "cancel=$([bool]$cancel) focused=$cancelFocused unchanged=$((Snapshot) -eq $before) focus=$(Focused)"

    # 2 - its Cancel button
    Send-ToNoto @() 0x2E
    Press (Wait-Mine $CT::Button 'Cancel')
    Add-Result $run '2  the confirmation''s Cancel writes nothing' ((Snapshot) -eq $before -and -not (Mine $CT::Button 'Delete folder')) "unchanged=$((Snapshot) -eq $before)"

    # 3 - an edited card is saved by Back; a failed save keeps the stack, so no delete can follow
    Focus-Row 'FolderList' 'Work'
    Send-ToNoto @(0x11) 0x28
    Focus-Row 'NoteList' 'Plan'
    Send-ToNoto @() 0x0D
    (Editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Plan typed before delete'); Start-Sleep -Milliseconds 300
    $null = Sql 'UPDATE Notes SET DeletedAt = $t WHERE Id = $n' @{ '$t' = $now; '$n' = $Plan }
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $stayed = (Visible-Id 'BackButton') -and -not (Visible-Id 'FolderList')
    $null = Sql 'UPDATE Notes SET DeletedAt = NULL WHERE Id = $n' @{ '$n' = $Plan }
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $saved = [string](Sql 'SELECT Content FROM Notes WHERE Id = $n' @{ '$n' = $Plan } -Scalar)
    Add-Result $run '3  a failed save keeps the stack (no route to delete); once it saves, Back reaches the folders' ($stayed -and (Visible-Id 'FolderList') -and $saved -eq 'Plan typed before delete' -and (Deleted 'Folders' $Work) -eq '') "stayed=$stayed saved=$saved"

    # 4 - the menu's Delete on Work: named notes, both binned with the folder, Home untouched, focus next
    Focus-Row 'FolderList' 'Work'
    Send-ToNoto @(0x10) 0x79
    (Wait-Mine $CT::MenuItem 'Delete').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900
    $message = Texts
    Press (Wait-Mine $CT::Button 'Delete folder')
    $stamps = [int](Sql 'SELECT COUNT(DISTINCT DeletedAt) FROM (SELECT DeletedAt FROM Folders WHERE Id = $w UNION ALL SELECT DeletedAt FROM Notes WHERE FolderId = $w)' @{ '$w' = $Work } -Scalar)
    Add-Result $run '4  Delete on Work names its 2 notes; bins the folder and both under one timestamp; focus to the next folder' ($message -match 'Work.*2 notes' -and (Deleted 'Folders' $Work) -ne '' -and (Deleted 'Notes' $Plan) -ne '' -and (Deleted 'Notes' $Todo) -ne '' -and $stamps -eq 1 -and (Deleted 'Notes' $Milk) -eq '' -and (I7) -and (Focused-Name) -eq 'Home' -and -not (Row 'FolderList' 'Work')) "stamps=$stamps i7=$(I7) focus=$(Focused)"

    # 5 - the Recycle Bin opens from its button
    $binButton = Find-ById 'RecycleBinButton'
    $binButton.SetFocus(); Start-Sleep -Milliseconds 200
    Press $binButton
    $null = Find-ById 'RecycleBinRoot'
    Start-Sleep -Milliseconds 500
    $entries = @(Rows 'RecycleBinList' | ForEach-Object { "$($_.Current.Name)=$($_.Current.HelpText)" })
    Add-Result $run '5  the Recycle Bin lists Work (2 notes) and its notes, with help text; focus on the first entry' (($entries -join '|') -eq 'Work=Folder, 2 notes|Plan typed before delete=Note in Work, which is in the Recycle Bin|Todo=Note in Work, which is in the Recycle Bin' -and (Focused-Name) -eq 'Work') "entries=$($entries -join '|') focus=$(Focused)"

    # 6 - a note whose folder is in the bin: no Restore, Restore to Home
    Focus-Row 'RecycleBinList' 'Plan typed before delete'
    $before = Snapshot
    Send-ToNoto @() 0x0D
    $enterWroteNothing = (Snapshot) -eq $before -and (Row 'RecycleBinList' 'Plan typed before delete') -and (Focused-Name) -eq 'Plan typed before delete'
    $restoreEnabled = (Find-ById 'BinRestoreButton').Current.IsEnabled
    Send-ToNoto @(0x10) 0x79
    $menu = Menu-Items
    (Wait-Mine $CT::MenuItem 'Restore to').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 600
    (Wait-Mine $CT::MenuItem 'Home').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900
    $planFolder = [string](Sql 'SELECT FolderId FROM Notes WHERE Id = $n' @{ '$n' = $Plan } -Scalar)
    Add-Result $run '6  a note whose folder is in the bin: Enter writes nothing; Restore disabled and not in its menu; Restore to Home keeps its text' ($enterWroteNothing -and -not $restoreEnabled -and ($menu -notcontains 'Restore') -and ($menu -contains 'Restore to') -and $planFolder -eq $Home_ -and (Deleted 'Notes' $Plan) -eq '' -and (I7)) "restoreEnabled=$restoreEnabled menu=$($menu -join ',') folder=$planFolder"

    # 7 - Enter on Work restores the folder with the rest of its notes
    Focus-Row 'RecycleBinList' 'Work'
    Send-ToNoto @() 0x0D
    Add-Result $run '7  Enter on Work restores the folder with Todo' ((Deleted 'Folders' $Work) -eq '' -and (Deleted 'Notes' $Todo) -eq '' -and (I7) -and (Rows 'RecycleBinList').Count -eq 0) "work=$((Deleted 'Folders' $Work) -eq '') todo=$((Deleted 'Notes' $Todo) -eq '') entries=$((Rows 'RecycleBinList').Count)"

    # 8 - Delete permanently on a folder: Cancel by default, then confirmed
    Send-ToNoto @() 0x1B
    Focus-Row 'FolderList' 'Empty'
    Send-ToNoto @() 0x2E
    Press (Wait-Mine $CT::Button 'Delete folder')
    $binButton = Find-ById 'RecycleBinButton'; $binButton.SetFocus(); Start-Sleep -Milliseconds 200; Press $binButton
    Focus-Row 'RecycleBinList' 'Empty'
    $before = Snapshot
    Send-ToNoto @() 0x2E
    Start-Sleep -Milliseconds 500
    $dialog = @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Button)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid -and $_.Current.Name -eq 'Delete permanently' -and $_.Current.AutomationId -ne 'BinPurgeButton' }).Count -gt 0
    Send-ToNoto @() 0x1B
    $kept = (Snapshot) -eq $before -and (Exists 'Folders' $Empty)
    Focus-Row 'RecycleBinList' 'Empty'
    Send-ToNoto @() 0x2E
    $buttons = @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Button)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid -and $_.Current.Name -eq 'Delete permanently' -and $_.Current.AutomationId -ne 'BinPurgeButton' })
    if ($buttons.Count) { Press $buttons[0] }
    Add-Result $run '8  Delete permanently asks (Escape writes nothing); confirmed, the folder is gone' ($dialog -and $kept -and -not (Exists 'Folders' $Empty)) "dialog=$([bool]$dialog) keptOnEscape=$kept gone=$(-not (Exists 'Folders' $Empty))"

    # 9 - Empty: Cancel, then confirmed; the empty state
    Send-ToNoto @() 0x1B
    Focus-Row 'FolderList' 'Work'
    Send-ToNoto @() 0x2E
    Press (Wait-Mine $CT::Button 'Delete folder')
    $binButton = Find-ById 'RecycleBinButton'; $binButton.SetFocus(); Start-Sleep -Milliseconds 200; Press $binButton
    $before = Snapshot
    Press (Find-ById 'EmptyBinButton')
    $cancel = Wait-Mine $CT::Button 'Cancel'
    if ($cancel) { Press $cancel }
    $keptOnCancel = (Snapshot) -eq $before
    Press (Find-ById 'EmptyBinButton')
    $confirm = @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Button)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid -and $_.Current.Name -eq 'Empty' })
    if ($confirm.Count) { Press $confirm[0] }
    $guidance = (Find-ById 'BinGuidanceText').Current.Name
    Add-Result $run '9  Empty: Cancel writes nothing; confirmed, everything in the bin is gone, the empty state shows' ($keptOnCancel -and -not (Exists 'Folders' $Work) -and -not (Exists 'Notes' $Todo) -and (Exists 'Notes' $Milk) -and (Exists 'Notes' $Plan) -and $guidance -match 'The Recycle Bin is empty' -and -not (Find-ById 'EmptyBinButton').Current.IsEnabled -and (I7)) "keptOnCancel=$keptOnCancel guidance=$guidance"

    # 11 - the bin's controls are named
    $named = @('BinBackButton', 'RecycleBinList', 'BinRestoreButton', 'BinPurgeButton', 'EmptyBinButton' | ForEach-Object { (Find-ById $_).Current.Name })
    Add-Result $run '11 the bin''s controls are named' (($named | Where-Object { -not $_ }).Count -eq 0) ($named -join ', ')

    # 10 - Escape leaves the bin, focus back on its button
    Send-ToNoto @() 0x1B
    Add-Result $run '10 Escape leaves the bin for the folder list, focus on the Recycle Bin button' ((Visible-Id 'FolderList') -and -not (Visible-Id 'RecycleBinRoot') -and (Focused) -match 'id=RecycleBinButton') "focus=$(Focused)"

    Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $script:noto) ''
    Write-Host '  [NOT RUN] command announcements: UI Automation notification events are not observable from this client'
})
