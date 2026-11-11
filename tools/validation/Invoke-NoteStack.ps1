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
     8  scrolling the stack to the end and back while editing: the text intact, and written by autosave (P2.1) while the
        card is still editing
     9  a failed save (the note binned before the typing): autosave fails without being asked, the card stays editing
        with its text, a notice, no navigation; Escape fails the same way; the editor usable; once it can, it saves
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
    22  the card menu's Pin: the card goes first, stored, help text "Pinned", focus on it; Unpin returns it
    23  Ctrl+Alt+Left folds the focused card, Ctrl+Alt+Right unfolds it: stored, help text, title kept, focus kept
    24  the card menu's Fold and Unfold
    25  Ctrl+Alt+Left in the editor with unsaved text: saved, folded, editing left, the card focused
    26  Enter on a folded card: it unfolds (stored) and edits
    27  Ctrl+Alt+Shift+Left folds every card, focus kept; Ctrl+Alt+Shift+Right unfolds every card
    28  pin a folded card: still folded, first; unpin: still folded
    29  far down the scrolled stack, Ctrl+Alt+Left folds the focused card and focus stays on it
    30  back on the folder list, the folder's row says its current note count, changed by the moves (#147); the folder
        menu's Pin and Unpin: the folder goes first with help text "Pinned", focus follows it
    31  Ctrl+Alt+Shift+Up on a focused card: it moves above the one before, stored, focus stays on it, nothing edits
    32  Ctrl+Alt+Shift+Down in the editor: moved down one, still editing with its text (written by autosave), focus in it
    33  the card menu's Move to top and Move to bottom
    34  the pin boundary: the first unpinned card does not rise above a pinned one; nothing is written
    35  at an edge, the menu's move is disabled
    36  Ctrl+Alt+Shift+Down on a folder moves it, focus follows; the folder menu's Move to top; Ctrl+Down still enters
    37  dragging a card above another: it lands there, stored, selected and focused
    38  dropping a card where it was: nothing is written
    39  dragging an unpinned card above a pinned one: refused, nothing written, the stack snaps back
    40  dragging the card being edited: it moves and keeps editing with its text (written by autosave)
    41  dragging far down the scrolled stack
    42  dragging a folder above another, then back
    43  one click on a card still edits it (drag enabled)
    44  holding Ctrl on the stack and on the folder list shows no automatic key-tip tooltip (B6, F1)
    45  from a folder row, Tab reaches the Recycle Bin button and Shift+Tab returns
    46  F7 (#150): Ctrl+3 colours a card Blue; the card's menu lists No colour and the six and colours it Pink; Ctrl+0
        clears; each is named in the card's help text. The card bar's Colour button colours the card being edited
        Green and Ctrl+0 clears it in the editor, which keeps editing with its text
    47  F6 (#149): the card bar's Fold saves, folds and leaves the card being edited; its "+ 3 lines" pill (named
        "Unfold, 3 lines hidden"; help text "Folded, 3 lines hidden") unfolds it, focus on the card. A folded one-line
        note shows no pill

  Autosave (P2.1) writes typed text after a second without a change. Every "saves first" case leaves the card within
  that second of setting its text, so the save it checks is the leave's own, not autosave's.

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
# Autosave (P2.1) writes after a second without a change: the row is given that, plus margin, to hold the text.
function Stored-Becomes([string] $Id, [string] $Text) {
    $deadline = (Get-Date).AddSeconds(4)
    while ((Get-Date) -lt $deadline) { if ((Stored $Id) -eq $Text) { return $true }; Start-Sleep -Milliseconds 200 }
    $false
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
function Pick([string] $Name) {
    <# A menu item that may be a radio item (the colours, F7): Invoke when it is offered, otherwise Toggle, which is what a
       radio menu item offers; either way its Click runs, as a press does. #>
    $e = Menu-Item $Name
    $patterns = @($e.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName })
    if ($patterns -contains 'InvokePatternIdentifiers.Pattern') { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    else { $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    Start-Sleep -Milliseconds 900
}
function ColourOf([string] $Id) { [string](Sql $script:data 'SELECT ColorKey FROM Notes WHERE Id = $n' @{ '$n' = $Id } -Scalar) }
function Flag([string] $Id, [string] $Column) { [int](Sql $script:data "SELECT $Column FROM Notes WHERE Id = `$n" @{ '$n' = $Id } -Scalar) }
function IdOf([string] $Content) { [string](Sql $script:data 'SELECT Id FROM Notes WHERE Content = $c AND DeletedAt IS NULL' @{ '$c' = $Content } -Scalar) }
function Focused-Name { $AE::FocusedElement.Current.Name }
function Focused-Help { $AE::FocusedElement.Current.HelpText }
function Card-Help([string] $Name) { $c = Card $Name; if ($c) { $c.Current.HelpText } else { '(no card)' } }
function Order { @(Sql-Rows 'SELECT Content FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder, Id' @{ '$f' = $FolderId }) }
function Sql-Rows([string] $Text, [hashtable] $Parameters) {
    $db = Join-Path $script:data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Text
        foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }
        $r = $cmd.ExecuteReader(); $out = [System.Collections.Generic.List[string]]::new()
        while ($r.Read()) { $out.Add($r.GetString(0)) }
        $r.Close(); $out
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function Above([string] $First, [string] $Second) { $o = Order; $o.IndexOf($First) -ge 0 -and $o.IndexOf($First) + 1 -eq $o.IndexOf($Second) }
function Menu-Enabled([string] $Name) { (Menu-Item $Name).Current.IsEnabled }
function Folder-Rows { @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)) | ForEach-Object { $_.Current.Name }) }
function Fence([int] $X, [int] $Y) {
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $X; $pt.Y = $Y
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $script:noto.Hwnd) { throw "refusing pointer input at ($X,$Y): it is not Noto" }
}
function Move-Pointer([int] $X, [int] $Y) {
    <# A real pointer move (MOUSEEVENTF_MOVE | ABSOLUTE | VIRTUALDESK): input the drag loop sees, unlike SetCursorPos. #>
    Add-Type -AssemblyName System.Windows.Forms
    $v = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $nx = [uint32][Math]::Round(($X - $v.Left) * 65535.0 / ($v.Width - 1)); $ny = [uint32][Math]::Round(($Y - $v.Top) * 65535.0 / ($v.Height - 1))
    [NotoVal.Win32]::mouse_event(0x0001 -bor 0x8000 -bor 0x4000, $nx, $ny, 0, [IntPtr]::Zero)
}
function Drag-To([int] $FromX, [int] $FromY, [int] $ToX, [int] $ToY) {
    <# A real mouse drag, only while Noto is in front and only where the window under each end is Noto. #>
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to drag: Noto is not the foreground window' }
    Fence $FromX $FromY; Fence $ToX $ToY
    Move-Pointer $FromX $FromY; Start-Sleep -Milliseconds 200
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 250
    for ($i = 1; $i -le 20; $i++) {
        Move-Pointer ([int]($FromX + ($ToX - $FromX) * $i / 20)) ([int]($FromY + ($ToY - $FromY) * $i / 20)); Start-Sleep -Milliseconds 50
    }
    Start-Sleep -Milliseconds 500
    [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
}
function Centre($Element) { $r = $Element.Current.BoundingRectangle; @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) }
function Grip($Element) { $r = $Element.Current.BoundingRectangle; @([int]($r.X + 6), [int]($r.Y + $r.Height / 2)) }
function UpperEdge($Element) { $r = $Element.Current.BoundingRectangle; @([int]($r.X + $r.Width / 2), [int]($r.Y + 3)) }
function Orders { (Sql-Rows 'SELECT Id || ''='' || SortOrder FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL ORDER BY Id' @{ '$f' = $FolderId }) -join '|' }
function Tooltips { @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ToolTip)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid -and -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }
function Hold-Control-Tooltips {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x11, $false))); Start-Sleep -Milliseconds 1500
    $seen = Tooltips
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x11, $true))); Start-Sleep -Milliseconds 300
    $seen
}
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

    $countAtStart = ActiveCount
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
    $written = Stored-Becomes $A 'Alpha draft while scrolling'
    Add-Result $run '8  scrolling away and back keeps the text, and autosave writes it while the card is still editing' ((Editor-Value) -eq 'Alpha draft while scrolling' -and $written -and (Editors).Count -eq 1) "value=$(Editor-Value) db=$(Stored $A) editors=$((Editors).Count) visibleAtEnd=$farEditors focus=$(Focused)"
    $inStack = (Focused) -match 'id=NoteEditor|ListItem|id=NoteList'
    Add-Result $run '8  focus stays in the stack after scrolling' $inStack "focus=$(Focused)"
    (Editors | Select-Object -First 1).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x1B
    Add-Result $run '8  ... then Escape leaves with the scrolled draft stored' ((Stored $A) -eq 'Alpha draft while scrolling' -and (Editors).Count -eq 0) "db=$(Stored $A) editors=$((Editors).Count)"

    # 9 - a failed save: the note binned underneath the editor before the typing, so autosave meets it first
    Edit-Card 'Gamma'
    $null = Sql $script:data 'UPDATE Notes SET DeletedAt = $t WHERE Id = $n' @{ '$t' = [DateTimeOffset]::UtcNow.ToString('O'); '$n' = $C }
    Set-EditorText 'Gamma unsaved'
    Start-Sleep -Milliseconds 1700
    $notice = Notice
    Add-Result $run '9  autosave meets a failed save unasked: still editing, text kept, a notice, nothing written' ((Editors).Count -eq 1 -and (Editor-Value) -eq 'Gamma unsaved' -and $notice -ne '' -and (Stored $C) -eq 'Gamma' -and (Focused) -match 'id=NoteEditor') "notice=$notice value=$(Editor-Value) db=$(Stored $C) focus=$(Focused)"
    Send-ToNoto @() 0x1B
    $notice = Notice
    Add-Result $run '9  Escape fails the same way: still editing, text kept, a notice, no navigation' ((Editors).Count -eq 1 -and (Editor-Value) -eq 'Gamma unsaved' -and $notice -ne '' -and (Visible-Id 'BackButton') -and -not (Visible-Id 'FolderList') -and (Focused) -match 'id=NoteEditor') "notice=$notice value=$(Editor-Value) focus=$(Focused)"
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
    Add-Result $run '12 Shift+F10 opens the card menu; Escape closes it' (($menu -join ',') -eq 'Edit,New note,Move to,Pin,Fold,Colour,Move to top,Move to bottom,Export as Markdown…,Move to the Recycle Bin' -and (Menu-Items).Count -eq 0 -and (Focused) -match 'ListItem') "menu=$($menu -join ',') after=$((Menu-Items).Count) focus=$(Focused)"

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
    Add-Result $run '16 folder menu (Open, Rename, New folder); Ctrl+N focuses the name box' (($menu -join ',') -eq 'Open,Rename,New folder,Pin,Move to top,Move to bottom,Export as Markdown…,Delete' -and (Focused) -match 'id=FolderNameInput') "menu=$($menu -join ',') focus=$(Focused)"

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

    # 22 - Pin and Unpin from the card menu
    $id7 = IdOf 'Filler 7'
    (Card 'Filler 7').SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Pin'
    $first = (Cards)[0].Current.Name
    Add-Result $run '22 Pin from the menu: first, stored, help text Pinned, focus on it' ($first -eq 'Filler 7' -and (Flag $id7 'IsPinned') -eq 1 -and (Card-Help 'Filler 7') -match 'Pinned' -and (Focused-Name) -eq 'Filler 7') "first=$first pinned=$(Flag $id7 'IsPinned') help=$(Card-Help 'Filler 7') focus=$(Focused)"
    Send-ToNoto @(0x10) 0x79
    $menu = Menu-Items
    Choose 'Unpin'
    $first = (Cards)[0].Current.Name
    Add-Result $run '22 ... the menu then offers Unpin, which returns it to its place' (($menu -contains 'Unpin') -and -not ($menu -contains 'Pin') -and $first -ne 'Filler 7' -and (Flag $id7 'IsPinned') -eq 0 -and (Focused-Name) -eq 'Filler 7') "menu=$($menu -join ',') first=$first focus=$(Focused)"

    # 23 - Ctrl+Alt+Left / Right on the focused card
    Send-ToNoto @(0x11, 0x12) 0x25
    $folded = (Flag $id7 'IsFolded') -eq 1; $help = Focused-Help; $name = Focused-Name
    Send-ToNoto @(0x11, 0x12) 0x27
    Add-Result $run '23 Ctrl+Alt+Left folds, Ctrl+Alt+Right unfolds: stored, help text, title and focus kept' ($folded -and $help -match 'Folded' -and $name -eq 'Filler 7' -and (Flag $id7 'IsFolded') -eq 0 -and (Focused-Name) -eq 'Filler 7' -and (Focused-Help) -notmatch 'Folded') "folded=$folded help=$help unfolded=$((Flag $id7 'IsFolded') -eq 0) focus=$(Focused)"

    # 24 - Fold and Unfold from the menu
    Send-ToNoto @(0x10) 0x79
    Choose 'Fold'
    $viaMenu = (Flag $id7 'IsFolded') -eq 1
    Send-ToNoto @(0x10) 0x79
    $menu = Menu-Items
    Choose 'Unfold'
    Add-Result $run '24 Fold and Unfold from the card menu' ($viaMenu -and ($menu -contains 'Unfold') -and (Flag $id7 'IsFolded') -eq 0 -and (Focused-Name) -eq 'Filler 7') "folded=$viaMenu menu=$($menu -join ',') focus=$(Focused)"

    # 25 - fold from the editor with unsaved text
    Edit-Card 'Filler 7'
    Set-EditorText 'Filler 7 typed then folded'
    Send-ToNoto @(0x11, 0x12) 0x25
    Add-Result $run '25 Ctrl+Alt+Left in the editor saves, folds, leaves editing, the card focused' ((Stored $id7) -eq 'Filler 7 typed then folded' -and (Flag $id7 'IsFolded') -eq 1 -and (Editors).Count -eq 0 -and (Focused-Name) -eq 'Filler 7 typed then folded') "db=$(Stored $id7) folded=$(Flag $id7 'IsFolded') editors=$((Editors).Count) focus=$(Focused)"

    # 26 - editing a folded card unfolds it
    Send-ToNoto @() 0x0D
    Add-Result $run '26 Enter on a folded card unfolds it (stored) and edits its whole text' ((Flag $id7 'IsFolded') -eq 0 -and (Editors).Count -eq 1 -and (Editor-Value) -eq 'Filler 7 typed then folded') "folded=$(Flag $id7 'IsFolded') value=$(Editor-Value)"
    Send-ToNoto @() 0x1B

    # 27 - fold all and unfold all
    $total = ActiveCount
    Send-ToNoto @(0x11, 0x12, 0x10) 0x25
    $foldedAll = [int](Sql $script:data 'SELECT COUNT(*) FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL AND IsFolded = 1' @{ '$f' = $FolderId } -Scalar)
    $keptFocus = Focused-Name
    Send-ToNoto @(0x11, 0x12, 0x10) 0x27
    $stillFolded = [int](Sql $script:data 'SELECT COUNT(*) FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL AND IsFolded = 1' @{ '$f' = $FolderId } -Scalar)
    $elsewhereFolded = [int](Sql $script:data 'SELECT COUNT(*) FROM Notes WHERE FolderId = $f AND IsFolded = 1' @{ '$f' = $Elsewhere } -Scalar)
    Add-Result $run '27 Ctrl+Alt+Shift+Left folds every card here (not elsewhere), focus kept; Right unfolds all' ($foldedAll -eq $total -and $keptFocus -eq 'Filler 7 typed then folded' -and $stillFolded -eq 0 -and $elsewhereFolded -eq 0) "folded=$foldedAll/$total focus=$keptFocus after=$stillFolded elsewhere=$elsewhereFolded"

    # 28 - pin a folded card, then unpin
    Send-ToNoto @(0x11, 0x12) 0x25
    Send-ToNoto @(0x10) 0x79
    Choose 'Pin'
    $first = (Cards)[0].Current.Name
    $both = (Flag $id7 'IsPinned') -eq 1 -and (Flag $id7 'IsFolded') -eq 1
    Send-ToNoto @(0x10) 0x79
    Choose 'Unpin'
    Add-Result $run '28 a folded card pinned stays folded and first; unpinned, still folded' ($both -and $first -eq 'Filler 7 typed then folded' -and (Flag $id7 'IsPinned') -eq 0 -and (Flag $id7 'IsFolded') -eq 1 -and (Card-Help 'Filler 7 typed then folded') -match 'Folded') "first=$first both=$both pinned=$(Flag $id7 'IsPinned') folded=$(Flag $id7 'IsFolded') focus=$(Focused)"
    Send-ToNoto @(0x11, 0x12) 0x27

    # 29 - far down the scrolled stack
    $list = Find-ById 'NoteList'
    $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 800
    $last = Cards | Where-Object { $_.Current.Name -like 'Filler *' } | Select-Object -Last 1
    $lastName = $last.Current.Name
    $last.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11, 0x12) 0x25
    $idLast = IdOf $lastName
    Add-Result $run '29 far down the scrolled stack, the focused card folds, keeps focus, and its container says Folded' ($idLast -ne '' -and (Flag $idLast 'IsFolded') -eq 1 -and (Focused-Name) -eq $lastName -and (Focused-Help) -match 'Folded') "card=$lastName folded=$(Flag $idLast 'IsFolded') help=$(Focused-Help) focus=$(Focused)"
    Send-ToNoto @(0x11, 0x12) 0x27
    $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 500

    # 30 - the folder menu's Pin and Unpin
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    # The folder rows' note counts (#147) are read again on the way back: cases 19-21 moved notes out of Stack, so its
    # count has changed since the folder list was first shown.
    $rows = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))
    $count = ActiveCount; $help = ($rows | Where-Object { $_.Current.Name -eq 'Stack' }).Current.HelpText
    Add-Result $run '30 back on the folder list, Stack says its current note count, changed since the start' ($count -ne $countAtStart -and $help -eq $(if ($count -eq 1) { '1 note' } else { "$count notes" })) "help='$help' active now=$count at start=$countAtStart"
    $rows = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))
    ($rows | Where-Object { $_.Current.Name -eq 'Elsewhere' }).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Pin'
    $firstFolder = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))[0].Current.Name
    $pinnedHelp = Focused-Help; $pinnedFocus = Focused-Name
    Send-ToNoto @(0x10) 0x79
    Choose 'Unpin'
    $firstAfter = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))[0].Current.Name
    Add-Result $run '30 folder Pin: first, help text Pinned, focus follows; Unpin returns it' ($firstFolder -eq 'Elsewhere' -and $pinnedHelp -match 'Pinned' -and $pinnedFocus -eq 'Elsewhere' -and $firstAfter -eq 'Stack' -and (Focused-Name) -eq 'Elsewhere') "first=$firstFolder help=$pinnedHelp focus=$pinnedFocus after=$firstAfter now=$(Focused)"
    Enter-Folder

    # 31 - Ctrl+Alt+Shift+Up on a focused card
    $wasAbove = Above 'Filler 8' 'Filler 9'
    (Card 'Filler 9').SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11, 0x12, 0x10) 0x26
    Add-Result $run '31 Ctrl+Alt+Shift+Up moves the card above the one before; focus stays; nothing edits' ($wasAbove -and (Above 'Filler 9' 'Filler 8') -and (Focused-Name) -eq 'Filler 9' -and (Editors).Count -eq 0) "before=$wasAbove now=$(Above 'Filler 9' 'Filler 8') focus=$(Focused) editors=$((Editors).Count)"

    # 32 - Ctrl+Alt+Shift+Down in the editor keeps editing and the text; autosave writes it, so the order is read by it
    $id10 = IdOf 'Filler 10'
    Edit-Card 'Filler 10'
    Set-EditorText 'Filler 10 unsaved while moving'
    Send-ToNoto @(0x11, 0x12, 0x10) 0x28
    $written = Stored-Becomes $id10 'Filler 10 unsaved while moving'
    Add-Result $run '32 Ctrl+Alt+Shift+Down in the editor: moved down, still editing, text kept and written by autosave, focus in it' ((Above 'Filler 11' 'Filler 10 unsaved while moving') -and $written -and (Editors).Count -eq 1 -and (Editor-Value) -eq 'Filler 10 unsaved while moving' -and (Focused) -match 'id=NoteEditor') "moved=$(Above 'Filler 11' 'Filler 10 unsaved while moving') value=$(Editor-Value) db=$(Stored $id10) focus=$(Focused)"
    Send-ToNoto @() 0x1B

    # 33 - Move to top and Move to bottom from the card menu
    (Card 'Filler 12').SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Move to top'
    $top = (Order)[0]; $focusTop = Focused-Name
    Send-ToNoto @(0x10) 0x79
    Choose 'Move to bottom'
    $bottom = (Order)[-1]
    Add-Result $run '33 Move to top, then Move to bottom, from the card menu; focus follows' ($top -eq 'Filler 12' -and $focusTop -eq 'Filler 12' -and $bottom -eq 'Filler 12' -and (Focused-Name) -eq 'Filler 12') "top=$top bottom=$bottom focus=$(Focused)"

    # 34 - the pin boundary
    $firstName = (Cards)[0].Current.Name
    (Card $firstName).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Pin'
    $nextName = (Cards)[1].Current.Name
    (Card $nextName).SetFocus(); Start-Sleep -Milliseconds 300
    $before = (Order) -join '|'
    Send-ToNoto @(0x11, 0x12, 0x10) 0x26
    Add-Result $run '34 the first unpinned card does not rise above a pinned one; nothing is written' (((Order) -join '|') -eq $before -and (Focused-Name) -eq $nextName) "pinned=$firstName unpinned=$nextName unchanged=$(((Order) -join '|') -eq $before) focus=$(Focused)"
    (Card $firstName).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Unpin'

    # 35 - at an edge the menu's move is disabled
    (Cards)[0].SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    $topEnabled = Menu-Enabled 'Move to top'; $bottomEnabled = Menu-Enabled 'Move to bottom'
    Send-ToNoto @() 0x1B
    Add-Result $run '35 on the first card, Move to top is disabled and Move to bottom is not' (-not $topEnabled -and $bottomEnabled) "top=$topEnabled bottom=$bottomEnabled"

    # 36 - folders: Ctrl+Alt+Shift+Down, the menu's Move to top, and Ctrl+Down still enters
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $start = (Folder-Rows) -join ','
    Send-ToNoto @(0x11, 0x12, 0x10) 0x28
    $afterKey = (Folder-Rows) -join ','; $keyFocus = Focused-Name
    Send-ToNoto @(0x10) 0x79
    Choose 'Move to top'
    $afterMenu = (Folder-Rows) -join ','
    Send-ToNoto @(0x11) 0x28
    $entered = Visible-Id 'BackButton'
    Add-Result $run '36 a folder moves by key and by menu, focus follows; Ctrl+Down still enters' ($start -eq 'Stack,Elsewhere' -and $afterKey -eq 'Elsewhere,Stack' -and $keyFocus -eq 'Stack' -and $afterMenu -eq 'Stack,Elsewhere' -and $entered) "start=$start key=$afterKey focus=$keyFocus menu=$afterMenu entered=$entered"

    # 37 - drag a card above another
    $o = Order
    $mover = $o[3]; $target = $o[1]
    $from = Grip (Card $mover); $to = UpperEdge (Card $target)
    Drag-To $from[0] $from[1] $to[0] $to[1]
    $after = Order
    Add-Result $run '37 dragging a card above another lands it there, stored, focused' ($after[1] -eq $mover -and $after[2] -eq $target -and (Focused-Name) -eq $mover) "mover=$mover target=$target order=$(($after | Select-Object -First 5) -join ',') focus=$(Focused)"

    # 38 - dropping where it was writes nothing
    $before = Orders
    $c = Card $after[2]; $g = Grip $c
    Drag-To $g[0] $g[1] ($g[0] + 30) ($g[1] + 2)
    Add-Result $run '38 dropping a card where it was writes nothing' ((Orders) -eq $before) "unchanged=$((Orders) -eq $before) focus=$(Focused)"

    # 39 - an unpinned card above a pinned one is refused
    $o = Order
    (Card $o[0]).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Pin'
    $before = Orders; $beforeOrder = (Order) -join '|'
    $from = Grip (Card $o[3]); $to = UpperEdge (Card $o[0])
    Drag-To $from[0] $from[1] $to[0] $to[1]
    $snapped = (Cards | Select-Object -First 4 | ForEach-Object { $_.Current.Name }) -join '|'
    Add-Result $run '39 an unpinned card dragged above a pinned one is refused: nothing written, the stack snaps back' ((Orders) -eq $before -and ((Order) -join '|') -eq $beforeOrder -and $snapped -eq (((Order) | Select-Object -First 4) -join '|')) "unchanged=$((Orders) -eq $before) shown=$snapped focus=$(Focused)"
    (Card $o[0]).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Choose 'Unpin'

    # 40 - drag the card being edited
    $o = Order
    $editingName = $o[4]
    $editId = IdOf $editingName
    Edit-Card $editingName
    Set-EditorText "$editingName, unsaved while dragged"
    $card = Cards | Where-Object { $_.Current.Name -eq $editingName } | Select-Object -First 1
    $from = Grip $card; $to = UpperEdge (Card $o[2])
    Drag-To $from[0] $from[1] $to[0] $to[1]
    $written = Stored-Becomes $editId "$editingName, unsaved while dragged"
    $after = Order
    Add-Result $run '40 the card being edited moves and keeps editing with its text, written by autosave' ($after[2] -eq "$editingName, unsaved while dragged" -and $written -and (Editors).Count -eq 1 -and (Editor-Value) -eq "$editingName, unsaved while dragged") "order=$(($after | Select-Object -First 5) -join ',') editors=$((Editors).Count) value=$(Editor-Value) db=$(Stored $editId)"
    Send-ToNoto @() 0x1B

    # 41 - far down the scrolled stack
    $list = Find-ById 'NoteList'
    $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 800
    $o = @(Order | Where-Object { $_ -like 'Filler *' })
    $mover = $o[-1]; $target = $o[-3]
    $from = Grip (Card $mover); $to = UpperEdge (Card $target)
    Drag-To $from[0] $from[1] $to[0] $to[1]
    $after = @(Order | Where-Object { $_ -like 'Filler *' })
    Add-Result $run '41 far down the scrolled stack, a dragged card lands where dropped' ($after[-3] -eq $mover -and $after[-2] -eq $target) "mover=$mover target=$target tail=$(($after | Select-Object -Last 4) -join ',') focus=$(Focused)"
    $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 500

    # 43 - one click on a card still edits it
    $c = (Cards)[2]; $name = $c.Current.Name; $xy = Centre $c
    Fence $xy[0] $xy[1]
    [void][NotoVal.Win32]::SetCursorPos($xy[0], $xy[1]); Start-Sleep -Milliseconds 150
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 900
    Add-Result $run '43 one click on a card still edits it' ((Editors).Count -eq 1 -and (Editor-Value) -eq $name -and (Focused) -match 'id=NoteEditor') "card=$name editors=$((Editors).Count) value=$(Editor-Value)"
    Send-ToNoto @() 0x1B

    # 42 - a folder dragged above another, then back
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $rows = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))
    $start = (Folder-Rows) -join ','
    $from = Grip ($rows | Where-Object { $_.Current.Name -eq 'Elsewhere' }); $to = UpperEdge ($rows | Where-Object { $_.Current.Name -eq 'Stack' })
    Drag-To $from[0] $from[1] $to[0] $to[1]
    $moved = (Folder-Rows) -join ','; $movedFocus = Focused-Name
    $rows = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))
    $from = Grip ($rows | Where-Object { $_.Current.Name -eq 'Stack' }); $to = UpperEdge ($rows | Where-Object { $_.Current.Name -eq 'Elsewhere' })
    Drag-To $from[0] $from[1] $to[0] $to[1]
    Add-Result $run '42 a folder dragged above another lands there, focused; and back' ($start -eq 'Stack,Elsewhere' -and $moved -eq 'Elsewhere,Stack' -and $movedFocus -eq 'Elsewhere' -and ((Folder-Rows) -join ',') -eq 'Stack,Elsewhere') "start=$start moved=$moved focus=$movedFocus back=$((Folder-Rows) -join ',')"
    Enter-Folder

    # 44 - no automatic key-tip tooltip while Ctrl is held
    (Cards)[0].SetFocus(); Start-Sleep -Milliseconds 300
    $onStack = @(Hold-Control-Tooltips)
    (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $onFolders = @(Hold-Control-Tooltips)
    Add-Result $run '44 holding Ctrl shows no automatic key-tip tooltip on the stack or the folder list' (($onStack | Where-Object { $_ -match 'Ctrl' }).Count -eq 0 -and ($onFolders | Where-Object { $_ -match 'Ctrl' }).Count -eq 0) "stack=$($onStack -join ',') folders=$($onFolders -join ',')"

    # 45 - Tab from a folder row reaches the Recycle Bin button; Shift+Tab returns
    $row = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))[0]
    $row.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x09
    $tabbed = Focused
    Send-ToNoto @(0x10) 0x09
    Add-Result $run '45 Tab from a folder row reaches the Recycle Bin button; Shift+Tab returns to the row' ($tabbed -match 'id=RecycleBinButton' -and (Focused) -match 'ListItem') "tab=$tabbed back=$(Focused)"
    Enter-Folder

    # 46 - F7 (#150): a note's colour from the keys, the card's menu and its bar; named in the card's help text
    $firstId = [string](Sql $script:data 'SELECT Id FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder, Id LIMIT 1' @{ '$f' = $FolderId } -Scalar)
    $first = (Cards)[0]; $firstName = $first.Current.Name
    $first.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x33   # Ctrl+3
    $byKey = ColourOf $firstId; $helpKey = Card-Help $firstName
    (Card $firstName).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x10) 0x79
    Expand 'Colour'
    $palette = @(Menu-Items | Where-Object { $_ -in 'No colour', 'Yellow', 'Green', 'Blue', 'Purple', 'Pink', 'Grey' })
    Pick 'Pink'
    $byMenu = ColourOf $firstId; $helpMenu = Card-Help $firstName
    (Card $firstName).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x30   # Ctrl+0
    $cleared = ColourOf $firstId; $helpCleared = Card-Help $firstName
    Add-Result $run '46 colour: Ctrl+3 is Blue, the menu lists no colour and the six, Pink from it, Ctrl+0 clears; each named in the help text' ($byKey -eq 'note3' -and $helpKey -match 'Blue' -and ($palette -join ',') -eq 'No colour,Yellow,Green,Blue,Purple,Pink,Grey' -and $byMenu -eq 'note5' -and $helpMenu -match 'Pink' -and $cleared -eq '' -and $helpCleared -notmatch 'Pink|Blue') "key=$byKey help='$helpKey' menu=$($palette -join ',') chosen=$byMenu help='$helpMenu' cleared='$cleared' help='$helpCleared'"
    Edit-Card 'Saved by Back'
    (Find-ById 'CardColorButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600
    Pick 'Green'
    $byBar = ColourOf $A; $editing = (Editors).Count -eq 1 -and (Editor-Value) -eq 'Saved by Back'
    (Editors | Select-Object -First 1).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x30   # Ctrl+0, in the editor
    $clearedInEditor = ColourOf $A; $stillEditing = (Editors).Count -eq 1 -and (Editor-Value) -eq 'Saved by Back'
    Add-Result $run '46 colour from the card bar (Green) and Ctrl+0 in the editor; the card keeps editing with its text' ($byBar -eq 'note2' -and $editing -and $clearedInEditor -eq '' -and $stillEditing) "bar=$byBar editing=$editing cleared='$clearedInEditor' still editing=$stillEditing"
    Send-ToNoto @() 0x1B

    # 47 - F6 (#149): the card bar's Fold; a folded card's "+ N lines" pill unfolds it; a one-line note has no pill
    Edit-Card 'Saved by Back'
    Set-EditorText "Saved by Back`rmilk`r`reggs`rbread"
    (Find-ById 'CardFoldButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900
    $foldedByBar = (Flag $A 'IsFolded') -eq 1; $savedText = (Stored $A) -match 'bread'; $left = (Editors).Count -eq 0
    $help = Card-Help 'Saved by Back'
    $pill = (Card 'Saved by Back').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'UnfoldPill'))
    $pillName = if ($pill) { $pill.Current.Name } else { '' }
    $pillText = if ($pill) { @($pill.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ' ' } else { '' }
    if ($pill) { $pill.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900 }
    Add-Result $run '47 the bar folds the card being edited (saved, left); its pill says "+ 3 lines" and unfolds it, focus on the card' ($foldedByBar -and $savedText -and $left -and $help -match 'Folded, 3 lines hidden' -and $pillName -eq 'Unfold, 3 lines hidden' -and $pillText -match '\+ 3 lines' -and (Flag $A 'IsFolded') -eq 0 -and (Card-Help 'Saved by Back') -notmatch 'Folded' -and (Focused-Name) -eq 'Saved by Back') "bar folded=$foldedByBar saved=$savedText left=$left help='$help' pill='$pillName' text='$pillText' unfolded=$((Flag $A 'IsFolded') -eq 0) focus=$(Focused)"
    $one = Stored $B   # one line: its title, as renamed by the cases above
    (Card $one).SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11, 0x12) 0x25   # Ctrl+Alt+Left
    $onePill = (Card $one).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'UnfoldPill'))
    $oneHelp = Card-Help $one; $oneFolded = (Flag $B 'IsFolded') -eq 1
    Send-ToNoto @(0x11, 0x12) 0x27   # Ctrl+Alt+Right
    Add-Result $run '47 a folded one-line note hides nothing: no pill, help text "Folded"' ($oneFolded -and (-not $onePill -or $onePill.Current.IsOffscreen) -and $oneHelp -eq 'Folded' -and (Flag $B 'IsFolded') -eq 0) "card='$one' folded=$oneFolded pill=$([bool]$onePill) help='$oneHelp' unfolded=$((Flag $B 'IsFolded') -eq 0)"

    # 18 - closing while editing saves, then exits cleanly
    Edit-Card 'Saved by Back'
    Set-EditorText 'Saved by close'
    $clean = Stop-Noto $script:noto
    Add-Result $run '18 closing while editing saves, then a clean exit' ($clean -and (Stored $A) -eq 'Saved by close') "clean=$clean db=$(Stored $A)"

    Write-Host '  [NOT RUN] command announcements: UI Automation notification events are not observable from this client'
})
