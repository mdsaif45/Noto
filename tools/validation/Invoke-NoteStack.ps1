#Requires -Version 7.4
<#
.SYNOPSIS
  The note stack (ADR-016): cards edited in place, one at a time, saved on leaving; the stack's keys and menu.

.DESCRIPTION
  A fresh isolated data root seeded with one folder of three notes. Keys reach Noto only while it is verifiably the
  foreground window (the activation hotkey arranges that). Nothing is typed into the editor: its text is set through
  UI Automation, as Invoke-EditorSaveOnClose does.

    1  entering the folder: three cards, read views only, focus on a card
    2  Enter on a focused card: that card edits, focus in its editor, which holds the card's text
    3  Escape in the editor: the text is saved, the card stays focused, no editor remains
    4  Down from a focused card: the next card is focused, nothing edits
    5  Ctrl+Alt+Down in the editor: the next card edits, the previous one's text saved
    6  Ctrl+N in the editor: the current card saved, one new note, editing, focus in it
    7  Ctrl+Alt+Backspace in the editor: that note goes to the recycle bin; focus stays in the stack
    8  Shift+F10 on a card: its menu (Edit, New note, Move to the Recycle Bin); Escape closes it
    9  Ctrl+, while editing, then Escape: back in the editor with its unsaved text
   10  Ctrl+W while editing with unsaved text: saved, then hidden
   11  Escape on a focused card: back to the folder list

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-NoteStack.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'note-stack'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$FolderId = '01J0000000000000000000F016'
$Seed = [ordered]@{ '01J00000000000000000000N01' = "Alpha`nsecond line"; '01J00000000000000000000N02' = 'Beta'; '01J00000000000000000000N03' = 'Gamma' }

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
function ActiveCount($Data) { [int](Sql $Data 'SELECT COUNT(*) FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL' @{ '$f' = $FolderId } -Scalar) }
function ContentOf($Data, [string] $Id) { [string](Sql $Data 'SELECT Content FROM Notes WHERE Id = $n' @{ '$n' = $Id } -Scalar) }

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
function Editors($Noto) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'NoteEditor')
    @($AE::FromHandle($Noto.Hwnd).FindAll($Scope::Descendants, $cond))
}
function Items($Noto, [string] $ListId) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)
    @((Find-ById $Noto $ListId).FindAll($Scope::Descendants, $cond))
}
function Focused { $f = $AE::FocusedElement; "$($f.Current.ControlType.ProgrammaticName)|id=$($f.Current.AutomationId)|name=$($f.Current.Name)" }
function Set-EditorText($Noto, [string] $Text) {
    $editor = Editors $Noto | Select-Object -First 1
    if (-not $editor) { throw 'no editor to set text in' }
    $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
    Start-Sleep -Milliseconds 300
}
function Send-ToNoto($Noto, [int[]] $Mods, [int] $Key) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $Noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 900
}
function Menu-Items {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::MenuItem)
    @($AE::RootElement.FindAll($Scope::Descendants, $cond) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid } | ForEach-Object { $_.Current.Name })
}

exit (Invoke-IsolatedRun $run -Title 'The note stack (ADR-016)' -Body {
    Assert-ChordFree $run
    $target = Start-TargetApp $run
    $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql $data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Stack', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }
    $i = 0
    foreach ($id in $Seed.Keys) {
        $i++
        $null = Sql $data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" @{ '$n' = $id; '$f' = $FolderId; '$c' = $Seed[$id]; '$s' = $i; '$t' = $now }
    }

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
    $noto = $script:noto
    $null = Set-ForegroundByKeyboard $target
    Send-ActivationChord -AllowedForeground $target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }

    # 1 - enter the folder
    $folder = (Items $noto 'FolderList')[0]
    $folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $folder.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto $noto @(0x11) 0x28
    $null = Find-ById $noto 'BackButton'
    Start-Sleep -Milliseconds 600
    $cards = Items $noto 'NoteList'
    $names = ($cards | ForEach-Object { $_.Current.Name }) -join ','
    Add-Result $run '1  three cards, no editor, focus on a card' ($cards.Count -eq 3 -and (Editors $noto).Count -eq 0 -and (Focused) -match 'ListItem') "cards=$($cards.Count) names=$names editors=$((Editors $noto).Count) focus=$(Focused)"

    # 2 - Enter edits the focused card
    $cards[0].SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto $noto @() 0x0D
    $editors = Editors $noto
    $value = if ($editors.Count -gt 0) { $editors[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -replace "`r", "`n" } else { '' }
    Add-Result $run '2  Enter edits the card: one editor, focused, holding its text' ($editors.Count -eq 1 -and (Focused) -match 'id=NoteEditor' -and $value -eq $Seed['01J00000000000000000000N01']) "editors=$($editors.Count) focus=$(Focused) value=$value"

    # 3 - Escape saves and leaves editing; the card stays focused
    Set-EditorText $noto 'Alpha edited'
    Send-ToNoto $noto @() 0x1B
    Add-Result $run '3  Escape saves, leaves editing, keeps the card focused' ((ContentOf $data '01J00000000000000000000N01') -eq 'Alpha edited' -and (Editors $noto).Count -eq 0 -and (Focused) -match 'ListItem\|.*name=Alpha edited') "db=$(ContentOf $data '01J00000000000000000000N01') editors=$((Editors $noto).Count) focus=$(Focused)"

    # 4 - Down moves focus, nothing edits
    Send-ToNoto $noto @() 0x28
    Add-Result $run '4  Down focuses the next card and edits nothing' ((Focused) -match 'name=Beta' -and (Editors $noto).Count -eq 0) "focus=$(Focused)"

    # 5 - Ctrl+Alt+Down in the editor: the next card edits, the previous saved
    Send-ToNoto $noto @() 0x0D
    Set-EditorText $noto 'Beta edited'
    Send-ToNoto $noto @(0x11, 0x12) 0x28
    $value = ((Editors $noto)[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)
    Add-Result $run '5  Ctrl+Alt+Down saves and edits the next card' ((ContentOf $data '01J00000000000000000000N02') -eq 'Beta edited' -and (Editors $noto).Count -eq 1 -and $value -eq 'Gamma' -and (Focused) -match 'id=NoteEditor') "db=$(ContentOf $data '01J00000000000000000000N02') value=$value focus=$(Focused)"

    # 6 - Ctrl+N in the editor
    Set-EditorText $noto 'Gamma edited'
    Send-ToNoto $noto @(0x11) 0x4E
    $value = ((Editors $noto)[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)
    Add-Result $run '6  Ctrl+N saves the card and edits one new note' ((ContentOf $data '01J00000000000000000000N03') -eq 'Gamma edited' -and (ActiveCount $data) -eq 4 -and (Editors $noto).Count -eq 1 -and $value -eq '' -and (Focused) -match 'id=NoteEditor') "db=$(ContentOf $data '01J00000000000000000000N03') notes=$(ActiveCount $data) value='$value' focus=$(Focused)"

    # 7 - Ctrl+Alt+Backspace in the editor deletes that note
    Send-ToNoto $noto @(0x11, 0x12) 0x08
    Add-Result $run '7  Ctrl+Alt+Backspace moves the note to the recycle bin; focus stays in the stack' ((ActiveCount $data) -eq 3 -and (Editors $noto).Count -eq 0 -and (Focused) -match 'ListItem|id=NoteList') "notes=$(ActiveCount $data) focus=$(Focused)"

    # 8 - Shift+F10 on a card
    (Items $noto 'NoteList')[0].SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto $noto @(0x10) 0x79
    $menu = Menu-Items
    Send-ToNoto $noto @() 0x1B
    Add-Result $run '8  Shift+F10 opens the card menu; Escape closes it' (($menu -join ',') -eq 'Edit,New note,Move to the Recycle Bin' -and (Menu-Items).Count -eq 0 -and (Focused) -match 'ListItem') "menu=$($menu -join ',') after=$((Menu-Items).Count) focus=$(Focused)"

    # 9 - Settings and back while editing
    Send-ToNoto $noto @() 0x0D
    Set-EditorText $noto 'Unsaved across settings'
    Send-ToNoto $noto @(0x11) 0xBC
    $settings = $true; try { $null = Find-ById $noto 'SettingsRoot' 5 } catch { $settings = $false }
    Send-ToNoto $noto @() 0x1B
    $value = if ((Editors $noto).Count) { (Editors $noto)[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } else { '' }
    Add-Result $run '9  Ctrl+, from the editor opens Settings; Escape returns to the editor with its text' ($settings -and $value -eq 'Unsaved across settings' -and (Focused) -match 'id=NoteEditor') "settings=$settings value=$value focus=$(Focused)"

    # 10 - Ctrl+W while editing saves first
    Send-ToNoto $noto @(0x11) 0x57
    Start-Sleep -Milliseconds 800
    $hidden = -not [NotoVal.Win32]::IsWindowVisible($noto.Hwnd)
    Add-Result $run '10 Ctrl+W saves the unsaved text and hides' ($hidden -and (ContentOf $data '01J00000000000000000000N01') -eq 'Unsaved across settings') "hidden=$hidden db=$(ContentOf $data '01J00000000000000000000N01')"

    # 11 - Escape on a focused card leaves the folder
    $null = Set-ForegroundByKeyboard $target
    Send-ActivationChord -AllowedForeground $target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $noto.Hwnd) { throw 'the hotkey did not bring Noto back' }
    Send-ToNoto $noto @() 0x1B
    Send-ToNoto $noto @() 0x1B
    $folders = $true; try { $null = Find-ById $noto 'FolderList' 5 } catch { $folders = $false }
    Add-Result $run '11 Escape leaves editing, then the folder' ($folders -and (Editors $noto).Count -eq 0) "folders=$folders focus=$(Focused)"

    Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $noto) ''
})
