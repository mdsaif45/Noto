#Requires -Version 7.4
<#
.SYNOPSIS
  The note stack (ADR-016): cards edited in place, one at a time, saved before every way out; keys, menus, focus.

.DESCRIPTION
  A fresh isolated data root seeded with one folder of 43 notes: Alpha, Beta and Gamma first, then 40 more, so the
  stack scrolls and its containers are recycled. Keys reach Noto only while it is verifiably the foreground window
  (the activation hotkey arranges that). Nothing is typed into the editor: its text is set through UI Automation, as
  Invoke-EditorSaveOnClose does. A failed save is the existing supported one: the note is binned underneath the
  editor, so UpdateNoteContent refuses (InvalidState).

     1  entering the folder: cards, no editor, focus on a card; folder and card names are the names shown
     2  Enter on a card: one editor, named "Note content", focused, holding exactly the card's text
     3  Escape: saved exactly, editing left, the card focused under its new title
     4  Down from a card: the next card focused, nothing edits
     5  choosing another card while editing (focus it, Enter): the first saved, the second edits, one editor
     6  Ctrl+Alt+Up back to it: its saved text, not the text read before the save
     7  Ctrl+N in the editor: the card saved exactly, one new note, editing, focused, one editor
     8  scrolling the stack to the end and back while editing: the unsaved text intact, nothing saved by scrolling
     9  a failed save: the card stays editing with its text, a notice, the editor usable, no navigation; then it saves
    10  Tab and Shift+Tab: out of the editor and back, the text intact
    11  Ctrl+Alt+Backspace in the editor: binned; focus stays in the stack
    12  Shift+F10 on a card: Edit, New note, Move to the Recycle Bin; Escape closes it
    13  Ctrl+, from the editor, then Escape: back in the editor with its unsaved text
    14  Ctrl+W while editing: saved, hidden; shown again: still editing that card
    15  Back while editing: saved; the folder list, focus on the folder
    16  the folder list still works: Shift+F10 (Open, Rename, New folder); Ctrl+N focuses the name box
    17  the New note button and Back are named
    18  closing while editing: saved, a clean exit
    19  Ctrl+Shift+M on a card: All folders (no recent yet), never the card's own; choosing one moves it, focus stays
    20  the card menu's Move to: the recent folder first; choosing it moves the card
    21  Ctrl+Shift+M from the editor with unsaved text: saved first, then moved

  Command announcements are UI Automation notification events, which the managed UI Automation client this harness
  uses cannot subscribe to: NOT RUN here, listened for in the release candidate's Narrator pass.

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
$Elsewhere = '01J0000000000000000000F017'
$A = '01J00000000000000000000N01'; $B = '01J00000000000000000000N02'; $C = '01J00000000000000000000N03'
$Seed = [ordered]@{ $A = 'Alpha'; $B = 'Beta'; $C = 'Gamma' }
foreach ($n in 4..43) { $Seed['01J0000000000000000000N{0:D3}' -f $n] = "Filler $n" }

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
function ActiveCount { [int](Sql $script:data 'SELECT COUNT(*) FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL' @{ '$f' = $FolderId } -Scalar) }
function Stored([string] $Id) { [string](Sql $script:data 'SELECT Content FROM Notes WHERE Id = $n' @{ '$n' = $Id } -Scalar) }

function By-Id([string] $Id) {
    $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id))
}
function Visible-Id([string] $Id) { $e = By-Id $Id; [bool]($e -and -not $e.Current.IsOffscreen) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Editors {
    @($AE::FromHandle($script:noto.Hwnd).FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'NoteEditor')))
}
function Editor-Value { $e = Editors | Select-Object -First 1; if ($e) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } else { $null } }
function Set-EditorText([string] $Text) {
    $e = Editors | Select-Object -First 1
    if (-not $e) { throw 'no editor to set text in' }
    $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
    Start-Sleep -Milliseconds 300
}
function Cards { @((Find-ById 'NoteList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) }
function Card([string] $Name) { Cards | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1 }
function Focused { $f = $AE::FocusedElement; "$($f.Current.ControlType.ProgrammaticName)|id=$($f.Current.AutomationId)|name=$($f.Current.Name)" }
function Notice { $e = By-Id 'NoteNoticeText'; if ($e -and -not $e.Current.IsOffscreen) { $e.Current.Name } else { '' } }
function Send-ToNoto([int[]] $Mods, [int] $Key) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 900
}
function Menu-Items {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::MenuItem)
    @($AE::RootElement.FindAll($Scope::Descendants, $cond) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid } | ForEach-Object { $_.Current.Name })
}
function FolderOf([string] $Id) { [string](Sql $script:data 'SELECT FolderId FROM Notes WHERE Id = $n' @{ '$n' = $Id } -Scalar) }
function Menu-Item([string] $Name) {
    $cond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::MenuItem),
        [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, $Name))
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        $e = @($AE::RootElement.FindAll($Scope::Descendants, $cond) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid }) | Select-Object -First 1
        if ($e) { return $e }
        Start-Sleep -Milliseconds 200
    }
    throw "no menu item '$Name'"
}
function Expand([string] $Name) { (Menu-Item $Name).GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 600 }
function Choose([string] $Name) { (Menu-Item $Name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900 }
function Bring-Forward {
    $null = Set-ForegroundByKeyboard $script:target
    Send-ActivationChord -AllowedForeground $script:target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}
function Enter-Folder {
    $folder = Find-ById 'FolderList' | ForEach-Object { $_.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)) }
    $folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $folder.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x28
    $null = Find-ById 'BackButton'
    Start-Sleep -Milliseconds 600
}
function Edit-Card([string] $Name) {
    $card = Card $Name
    if (-not $card) { throw "no card '$Name' is realised" }
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x0D
}

exit (Invoke-IsolatedRun $run -Title 'The note stack (ADR-016)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Stack', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }
    $null = Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Elsewhere', 2, `$t, `$t)" @{ '$f' = $Elsewhere; '$t' = $now }
    $i = 0
    foreach ($id in $Seed.Keys) {
        $i++
        $null = Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" @{ '$n' = $id; '$f' = $FolderId; '$c' = $Seed[$id]; '$s' = $i; '$t' = $now }
    }

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward

    # 1 - enter the folder
    $folderName = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)).Current.Name
    Enter-Folder
    $names = @(Cards | Select-Object -First 3 | ForEach-Object { $_.Current.Name }) -join ','
    Add-Result $run '1  cards, no editor, focus on a card; folder and card names as shown' ($folderName -eq 'Stack' -and $names -eq 'Alpha,Beta,Gamma' -and (Editors).Count -eq 0 -and (Focused) -match 'ListItem') "folder=$folderName first=$names realised=$((Cards).Count) editors=$((Editors).Count) focus=$(Focused)"

    # 2 - Enter edits the focused card
    Edit-Card 'Alpha'
    $editor = Editors | Select-Object -First 1
    Add-Result $run '2  Enter edits: one editor named "Note content", focused, holding the text' ((Editors).Count -eq 1 -and $editor.Current.Name -eq 'Note content' -and (Focused) -match 'id=NoteEditor' -and (Editor-Value) -eq 'Alpha') "editors=$((Editors).Count) name=$($editor.Current.Name) focus=$(Focused) value=$(Editor-Value)"

    # 3 - Escape saves exactly and leaves editing; the card stays focused under its new title
    Set-EditorText 'Alpha · edited ✓'
    Send-ToNoto @() 0x1B
    Add-Result $run '3  Escape saves exactly, leaves editing, the card focused' ((Stored $A) -eq 'Alpha · edited ✓' -and (Editors).Count -eq 0 -and (Focused) -match 'ListItem\|.*name=Alpha · edited ✓') "db=$(Stored $A) editors=$((Editors).Count) focus=$(Focused)"

    # 4 - Down moves focus, nothing edits
    Send-ToNoto @() 0x28
    Add-Result $run '4  Down focuses the next card and edits nothing' ((Focused) -match 'name=Beta' -and (Editors).Count -eq 0) "focus=$(Focused)"

    # 5 - choosing another card while editing saves the first
    Send-ToNoto @() 0x0D
    Set-EditorText 'Beta chosen away'
    Edit-Card 'Gamma'
    Add-Result $run '5  choosing another card saves the first; it edits; one editor' ((Stored $B) -eq 'Beta chosen away' -and (Editors).Count -eq 1 -and (Editor-Value) -eq 'Gamma' -and (Focused) -match 'id=NoteEditor') "db=$(Stored $B) editors=$((Editors).Count) value=$(Editor-Value) focus=$(Focused)"

    # 6 - back to it: its saved text, not the read before the save
    Send-ToNoto @(0x11, 0x12) 0x26
    Add-Result $run '6  Ctrl+Alt+Up returns to it with its saved text, not stale text' ((Editor-Value) -eq 'Beta chosen away' -and (Editors).Count -eq 1 -and (Focused) -match 'id=NoteEditor') "value=$(Editor-Value) focus=$(Focused)"

    # 7 - Ctrl+N in the editor saves exactly, then edits one new note
    Set-EditorText 'Beta before new · exact'
    $before = ActiveCount
    Send-ToNoto @(0x11) 0x4E
    $selected = (Find-ById 'NoteList').GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name }
    Add-Result $run '7  Ctrl+N saves the card exactly, then edits one new note' ((Stored $B) -eq 'Beta before new · exact' -and (ActiveCount) -eq $before + 1 -and (Editors).Count -eq 1 -and (Editor-Value) -eq '' -and (Focused) -match 'id=NoteEditor' -and $selected -eq 'Untitled note') "db=$(Stored $B) notes=$before->$(ActiveCount) value='$(Editor-Value)' selected=$selected focus=$(Focused)"
    Send-ToNoto @() 0x1B

    # 8 - scroll away and back while editing
    $list = Find-ById 'NoteList'
    $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 500
    Edit-Card 'Alpha · edited ✓'
    Set-EditorText 'Alpha draft while scrolling'
    $scroll = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $scroll.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 800
    $farEditors = (Editors | Where-Object { -not $_.Current.IsOffscreen }).Count
    $scroll.SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 800
    Add-Result $run '8  scrolling away and back keeps the unsaved text; scrolling saves nothing' ((Editor-Value) -eq 'Alpha draft while scrolling' -and (Stored $A) -eq 'Alpha · edited ✓' -and (Editors).Count -eq 1) "value=$(Editor-Value) db=$(Stored $A) editors=$((Editors).Count) visibleAtEnd=$farEditors focus=$(Focused)"
    $inStack = (Focused) -match 'id=NoteEditor|ListItem|id=NoteList'
    Add-Result $run '8  focus stays in the stack after scrolling' $inStack "focus=$(Focused)"
    (Editors | Select-Object -First 1).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x1B
    Add-Result $run '8  ... then Escape saves the scrolled draft' ((Stored $A) -eq 'Alpha draft while scrolling') "db=$(Stored $A)"

    # 9 - a failed save: the note binned underneath the editor
    Edit-Card 'Gamma'
    Set-EditorText 'Gamma unsaved'
    $null = Sql $script:data 'UPDATE Notes SET DeletedAt = $t WHERE Id = $n' @{ '$t' = [DateTimeOffset]::UtcNow.ToString('O'); '$n' = $C }
    Send-ToNoto @() 0x1B
    $notice = Notice
    Add-Result $run '9  a failed save: still editing, text kept, a notice, no navigation' ((Editors).Count -eq 1 -and (Editor-Value) -eq 'Gamma unsaved' -and $notice -ne '' -and (Visible-Id 'BackButton') -and -not (Visible-Id 'FolderList') -and (Focused) -match 'id=NoteEditor') "notice=$notice value=$(Editor-Value) focus=$(Focused)"
    Set-EditorText 'Gamma unsaved, still typing'
    Add-Result $run '9  ... the editor stays usable' ((Editor-Value) -eq 'Gamma unsaved, still typing') "value=$(Editor-Value)"
    $null = Sql $script:data 'UPDATE Notes SET DeletedAt = NULL WHERE Id = $n' @{ '$n' = $C }
    Send-ToNoto @() 0x1B
    Add-Result $run '9  ... and once it can, Escape saves it and the notice goes' ((Stored $C) -eq 'Gamma unsaved, still typing' -and (Editors).Count -eq 0 -and (Notice) -eq '') "db=$(Stored $C) notice=$(Notice)"

    # 10 - Tab out of the editor and Shift+Tab back
    Edit-Card 'Gamma unsaved, still typing'
    Set-EditorText 'Gamma tabbed'
    Send-ToNoto @() 0x09
    $out = Focused
    Send-ToNoto @(0x10) 0x09
    Add-Result $run '10 Tab leaves the editor, Shift+Tab returns, text intact' ($out -notmatch 'id=NoteEditor' -and (Focused) -match 'id=NoteEditor' -and (Editor-Value) -eq 'Gamma tabbed') "tab=$out back=$(Focused) value=$(Editor-Value)"

    # 11 - Ctrl+Alt+Backspace in the editor
    $before = ActiveCount
    Send-ToNoto @(0x11, 0x12) 0x08
    Add-Result $run '11 Ctrl+Alt+Backspace bins the note; focus stays in the stack' ((ActiveCount) -eq $before - 1 -and (Editors).Count -eq 0 -and (Focused) -match 'ListItem|id=NoteList') "notes=$before->$(ActiveCount) focus=$(Focused)"

    # 12 - Shift+F10 on a card
    (Cards)[0].SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    $menu = Menu-Items
    Send-ToNoto @() 0x1B
    Add-Result $run '12 Shift+F10 opens the card menu; Escape closes it' (($menu -join ',') -eq 'Edit,New note,Move to,Move to the Recycle Bin' -and (Menu-Items).Count -eq 0 -and (Focused) -match 'ListItem') "menu=$($menu -join ',') after=$((Menu-Items).Count) focus=$(Focused)"

    # 13 - Settings and back while editing
    Send-ToNoto @() 0x0D
    Set-EditorText 'Unsaved across settings'
    Send-ToNoto @(0x11) 0xBC
    $settings = Visible-Id 'SettingsRoot'
    Send-ToNoto @() 0x1B
    Add-Result $run '13 Ctrl+, from the editor; Escape returns to it with its text' ($settings -and (Editor-Value) -eq 'Unsaved across settings' -and (Focused) -match 'id=NoteEditor') "settings=$settings value=$(Editor-Value) focus=$(Focused)"

    # 14 - Ctrl+W saves and hides; shown again, still editing
    $editing = (Cards)[0].Current.Name
    Send-ToNoto @(0x11) 0x57
    Start-Sleep -Milliseconds 800
    $hidden = -not [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd)
    $saved = (Stored $A) -eq 'Unsaved across settings'
    Bring-Forward
    Add-Result $run '14 Ctrl+W saves and hides; shown again, still editing it' ($hidden -and $saved -and (Editors).Count -eq 1 -and (Editor-Value) -eq 'Unsaved across settings') "card=$editing hidden=$hidden db=$(Stored $A) editors=$((Editors).Count) focus=$(Focused)"

    # 15 - Back while editing saves, then the folder list
    Set-EditorText 'Saved by Back'
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    Add-Result $run '15 Back saves, then the folder list with focus on the folder' ((Stored $A) -eq 'Saved by Back' -and (Visible-Id 'FolderList') -and (Focused) -match 'ListItem\|.*name=Stack') "db=$(Stored $A) focus=$(Focused)"

    # 16 - the folder list still works
    Send-ToNoto @(0x10) 0x79
    $menu = Menu-Items
    Send-ToNoto @() 0x1B
    Send-ToNoto @(0x11) 0x4E
    Add-Result $run '16 folder menu (Open, Rename, New folder); Ctrl+N focuses the name box' (($menu -join ',') -eq 'Open,Rename,New folder' -and (Focused) -match 'id=FolderNameInput') "menu=$($menu -join ',') focus=$(Focused)"

    # 17 - named actions
    Enter-Folder
    $new = Find-ById 'NewNoteButton'; $back = Find-ById 'BackButton'
    Add-Result $run '17 New note and Back are named' ($new.Current.Name -eq 'New note' -and $back.Current.Name -eq 'Back to folders') "new=$($new.Current.Name) back=$($back.Current.Name)"

    # 19 - Ctrl+Shift+M on a card: no recent yet, All folders without the card's own; choose one
    $card = Card 'Filler 4'
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    $before = ActiveCount
    Send-ToNoto @(0x11, 0x10) 0x4D
    $top = Menu-Items
    Expand 'All folders'
    $all = @(Menu-Items | Where-Object { $_ -notin $top })
    Choose 'Elsewhere'
    $id4 = [string](Sql $script:data "SELECT Id FROM Notes WHERE Content = 'Filler 4'" -Scalar)
    Add-Result $run '19 Ctrl+Shift+M: All folders without its own; choosing one moves it; focus stays' (($top -join ',') -eq 'All folders' -and ($all -join ',') -eq 'Elsewhere' -and (FolderOf $id4) -eq $Elsewhere -and (ActiveCount) -eq $before - 1 -and -not (Card 'Filler 4') -and (Focused) -match 'ListItem') "top=$($top -join ',') all=$($all -join ',') folder=$(FolderOf $id4) notes=$before->$(ActiveCount) focus=$(Focused)"

    # 20 - the card menu's Move to: the recent folder first
    $card = Card 'Filler 5'
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Expand 'Move to'
    $sub = @(Menu-Items | Where-Object { $_ -notin 'Edit', 'New note', 'Move to', 'Move to the Recycle Bin' })
    Choose 'Elsewhere'
    $id5 = [string](Sql $script:data "SELECT Id FROM Notes WHERE Content = 'Filler 5'" -Scalar)
    Add-Result $run '20 the card menu''s Move to lists the recent folder first; choosing it moves the card' ($sub[0] -eq 'Elsewhere' -and (FolderOf $id5) -eq $Elsewhere -and -not (Card 'Filler 5')) "sub=$($sub -join ',') folder=$(FolderOf $id5) focus=$(Focused)"

    # 21 - Ctrl+Shift+M from the editor with unsaved text: saved first, then moved
    Edit-Card 'Filler 6'
    Set-EditorText 'Filler 6, typed then moved'
    Send-ToNoto @(0x11, 0x10) 0x4D
    Choose 'Elsewhere'
    $id6 = [string](Sql $script:data "SELECT Id FROM Notes WHERE Content = 'Filler 6, typed then moved'" -Scalar)
    Add-Result $run '21 Ctrl+Shift+M from the editor saves the text, then moves the card' ($id6 -ne '' -and (FolderOf $id6) -eq $Elsewhere -and (Editors).Count -eq 0 -and (Focused) -match 'ListItem') "id=$id6 folder=$(FolderOf $id6) editors=$((Editors).Count) focus=$(Focused)"

    # 18 - closing while editing saves, then exits cleanly
    Edit-Card 'Saved by Back'
    Set-EditorText 'Saved by close'
    $clean = Stop-Noto $script:noto
    Add-Result $run '18 closing while editing saves, then a clean exit' ($clean -and (Stored $A) -eq 'Saved by close') "clean=$clean db=$(Stored $A)"

    Write-Host '  [NOT RUN] command announcements: UI Automation notification events are not observable from this client'
})
