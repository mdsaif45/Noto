#Requires -Version 7.4
<#
.SYNOPSIS
  The markdown editor (ADR-017): rendered cards, hidden markup, typing, formatting keys and menu, undo and redo, lists,
  paste, saving, switching and reopening, Light and Dark, and what opening and typing cost on realistic notes.

.DESCRIPTION
  A fresh isolated data root seeded with one folder of realistic notes, plus four long realistic notes (5, 20, 50 and
  100 KB). Keys reach Noto only while it is verifiably the foreground window (the activation hotkey arranges that).
  Text is set through UI Automation's Value pattern, which the editor answers with its markdown source, or typed as
  real key input. The clipboard is used for case 9 and restored afterwards.

     1  the stack shows each card rendered: no markdown markup in its read view, headings and checkboxes drawn
     2  Enter edits a card: one editor named "Note content", Text and Value patterns, its Value the stored source
     3  in the editor, markup is hidden and formatting shown: '**' hidden, its text bold, a heading larger
     4  typing at the end of the note adds exactly the typed text
     5  Ctrl+Shift+Left then Ctrl+B makes the word bold: '**word**' in the source, the markers hidden
     6  Ctrl+Z undoes the bold, Ctrl+Y redoes it
     7  Enter in a list item continues the list; Enter on an empty item ends it
     8  the Format button opens one menu with every command and its key; Checklist makes the line a task
     9  Ctrl+V pastes plain text, the clipboard's own line breaks kept as LF
    10  a second without typing writes the note; Escape leaves and the card shows it rendered
    11  closing Noto and opening it again: the same source in the database and in the editor
    12  Dark: the same, rendered and edited, after a restart in the Dark theme (screenshots of both themes in the run)
    13  cost: for 5, 20, 50 and 100 KB realistic notes, Noto's own load and keystroke traces (Noto.Editor loaded / change)

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Editor.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
Add-Type -AssemblyName System.Drawing
if (-not ('NotoVal.Capture' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace NotoVal {
    public static class Capture {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        // PW_RENDERFULLCONTENT (2): the composited content, which is what a WinUI window draws.
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    }
}
'@
}
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'editor'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$TP = [System.Windows.Automation.TextPattern]
$FolderId = '01J0000000000000000000F018'
$Meeting = '01J00000000000000000000E01'; $Groceries = '01J00000000000000000000E02'; $Plain = '01J00000000000000000000E03'
$MeetingText = "## Weekly sync`n`n**Attendees:** Priya, Tom, me`n`n- [ ] Tom: fix the *startup* crash`n- [x] book the room`n`nSee [the RFC](https://example.com/rfc) and ``release.md``."
$Seed = [ordered]@{
    $Meeting   = $MeetingText
    $Groceries = "# Groceries`n- [ ] milk`n- [x] bread`n- [ ] **coffee**"
    $Plain     = 'A plain note with no markup at all.'
}
$Chunk = $MeetingText + "`n`nPlain paragraph text that goes on for a while, the way a long note reads, with a **bold** phrase and a [link](https://example.com) now and then.`n`n- [ ] another task`n1. a numbered step`n> a quote`n`n"
$Sizes = [ordered]@{ '5 KB' = 5000; '20 KB' = 20000; '50 KB' = 50000; '100 KB' = 100000 }
$i = 0
foreach ($size in $Sizes.Keys) { $i++; $Seed['01J00000000000000000000P{0:D2}' -f $i] = "Long note $size`n`n" + ($Chunk * [Math]::Ceiling($Sizes[$size] / $Chunk.Length)) }

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
function Stored([string] $Id) { [string](Sql $script:data 'SELECT Content FROM Notes WHERE Id = $n' @{ '$n' = $Id } -Scalar) }
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Editor { Find-ById 'NoteEditor' }
function Editor-Value { (Editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Set-EditorText([string] $Text) { (Editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text); Start-Sleep -Milliseconds 300 }
function Cards { @((Find-ById 'NoteList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) }
function Card([string] $Name) { Cards | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1 }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 500) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Type-Text([string] $Text) {
    <# Real key input, one Unicode character at a time (KEYEVENTF_UNICODE), only while Noto is in front. #>
    foreach ($ch in $Text.ToCharArray()) {
        if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to type: Noto is not the foreground window' }
        $down = New-Object NotoVal.Win32+INPUT; $down.type = 1; $down.wScan = [uint16]$ch; $down.dwFlags = 4
        $up = New-Object NotoVal.Win32+INPUT; $up.type = 1; $up.wScan = [uint16]$ch; $up.dwFlags = 6
        [NotoVal.Win32]::Keys(@($down, $up)); Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 300
}
function Bring-Forward {
    $null = Set-ForegroundByKeyboard $script:target
    Send-ActivationChord -AllowedForeground $script:target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}
function Enter-Folder {
    $folder = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $folder.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x28 800
    $null = Find-ById 'BackButton'
}
function Edit-Card([string] $Name) {
    $card = Card $Name
    if (-not $card) { throw "no card '$Name' is realised" }
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x0D 900
}
function Read-Text($Card) {
    <# What a card's read view renders: the RichTextBlock's text, as UI Automation reads it. #>
    $views = @($Card.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ClassNameProperty, 'RichTextBlock')))
    if ($views.Count -eq 0) { return $null }
    $views[0].GetCurrentPattern($TP::Pattern).DocumentRange.GetText(-1)
}
function Range-Of([string] $Needle) {
    <# The editor's Text pattern range holding $Needle, or $null. #>
    (Editor).GetCurrentPattern($TP::Pattern).DocumentRange.FindText($Needle, $false, $false)
}
function Shot([string] $Name) {
    $r = New-Object NotoVal.Capture+RECT; [void][NotoVal.Capture]::GetWindowRect($script:noto.Hwnd, [ref]$r)
    $bmp = [System.Drawing.Bitmap]::new([Math]::Max(1, $r.R - $r.L), [Math]::Max(1, $r.B - $r.T))
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { [void][NotoVal.Capture]::PrintWindow($script:noto.Hwnd, $dc, 2) } finally { $g.ReleaseHdc($dc); $g.Dispose() }
    $bmp.Save((Join-Path $run.Root "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "$Name.png"
}
function Traces([string] $Pattern) { @($script:log.Lines($script:noto.Pid) | Where-Object { $_ -match $Pattern }) }
function Ms([string] $Line) { if ($Line -match 'ms=([0-9.]+)') { [double]$Matches[1] } else { $null } }
function Start-Fresh([string] $Theme) {
    Set-NotoSetting $run -Exe $NotoExe -DataRoot $script:data -Key 'appearance.theme' -Value $Theme
    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward
    Enter-Folder
}

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'The markdown editor (ADR-017)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Editor', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }
    $order = 0
    foreach ($id in $Seed.Keys) {
        $order++
        $null = Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" @{ '$n' = $id; '$f' = $FolderId; '$c' = $Seed[$id]; '$s' = $order; '$t' = $now }
    }

    Start-Fresh 'Light'

    # 1 - rendered cards
    $meetingRead = Read-Text (Card 'Weekly sync')
    $groceryRead = Read-Text (Card 'Groceries')
    Add-Result $run '1  cards are rendered: no markup in a read view; checkboxes drawn' ($meetingRead -and $meetingRead -notmatch '\*\*|\]\(|##' -and $meetingRead -match 'Attendees:' -and $groceryRead -match '☐' -and $groceryRead -match '☑' -and $groceryRead -notmatch '\[ \]') "meeting='$($meetingRead -replace "`r|`n", ' ')' groceries='$($groceryRead -replace "`r|`n", ' ')'"
    $lightStack = Shot 'light-stack'

    # 2 - the editor
    Edit-Card 'Weekly sync'
    $editor = Editor
    $patterns = ($editor.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }) -join ','
    Add-Result $run '2  Enter edits: one editor "Note content" with Text and Value patterns, holding the stored source' ($editor.Current.Name -eq 'Note content' -and $patterns -match 'TextPattern' -and $patterns -match 'ValuePattern' -and (Editor-Value) -eq $MeetingText) "name=$($editor.Current.Name) patterns=$patterns"
    $lightEdit = Shot 'light-editing'

    # 3 - hidden markup, shown formatting
    $marks = Range-Of '**'
    $inner = Range-Of 'Attendees:'
    $hidden = if ($marks) { $marks.GetAttributeValue($TP::IsHiddenAttribute) } else { 'no range' }
    $weight = if ($inner) { $inner.GetAttributeValue($TP::FontWeightAttribute) } else { 0 }
    $headingSize = (Range-Of 'Weekly sync').GetAttributeValue($TP::FontSizeAttribute)
    $bodySize = (Range-Of 'Priya').GetAttributeValue($TP::FontSizeAttribute)
    Add-Result $run '3  markup hidden, its text bold, the heading larger than body text' ($hidden -eq $true -and [int]$weight -ge 600 -and [double]$headingSize -gt [double]$bodySize) "hidden=$hidden weight=$weight heading=$headingSize body=$bodySize"

    # 4 - typing
    Send-ToNoto @(0x11) 0x23 300
    Type-Text ' great'
    Add-Result $run '4  typing at the end adds exactly the typed text' ((Editor-Value) -eq "$MeetingText great") "tail='$((Editor-Value).Substring([Math]::Max(0, (Editor-Value).Length - 20)))'"

    # 5 - Ctrl+B on the word just typed
    Send-ToNoto @(0x11, 0x10) 0x25 300
    Send-ToNoto @(0x11) 0x42
    $boldTail = (Editor-Value).EndsWith('**great**')
    $markerHidden = (Range-Of '**great**')
    Add-Result $run '5  Ctrl+B bolds the selected word: the source holds **great**' $boldTail "tail='$((Editor-Value).Substring([Math]::Max(0, (Editor-Value).Length - 20)))'"

    # 6 - undo, redo
    Send-ToNoto @(0x11) 0x5A
    $undone = (Editor-Value) -eq "$MeetingText great"
    Send-ToNoto @(0x11) 0x59
    $redone = (Editor-Value).EndsWith('**great**')
    Add-Result $run '6  Ctrl+Z undoes the bold, Ctrl+Y redoes it' ($undone -and $redone) "undone=$undone redone=$redone"

    # 7 - lists continue and end
    Send-ToNoto @(0x11) 0x23 300
    Send-ToNoto @() 0x0D 300
    Type-Text '- milk'
    Send-ToNoto @() 0x0D 400
    $continued = (Editor-Value).EndsWith("- milk`n- ")
    Send-ToNoto @() 0x0D 400
    $ended = (Editor-Value).EndsWith("- milk`n")
    Add-Result $run '7  Enter continues a list; Enter on an empty item ends it' ($continued -and $ended) "continued=$continued ended=$ended"

    # 8 - the Format menu
    Type-Text 'call the dentist'
    (Find-ById 'CardFormatButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    $items = @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::MenuItem)) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid } | ForEach-Object { $_.Current.Name })
    $expected = 'Bold', 'Italic', 'Underline', 'Strikethrough', 'Code', 'Link', 'Heading', 'Bulleted list', 'Numbered list', 'Checklist', 'Check or uncheck', 'Code block', 'Show or hide markup'
    $checklist = @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Checklist')) | Where-Object { $_.Current.ProcessId -eq $script:noto.Pid })[0]
    $checklist.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    Add-Result $run '8  one Format menu with every command; Checklist makes the line a task; focus back in the editor' ((@($expected | Where-Object { $items -notcontains $_ }).Count -eq 0) -and (Editor-Value).EndsWith('- [ ] call the dentist') -and $AE::FocusedElement.Current.AutomationId -eq 'NoteEditor') "items=$($items -join '|') tail='$((Editor-Value).Substring([Math]::Max(0, (Editor-Value).Length - 25)))' focus=$($AE::FocusedElement.Current.AutomationId)"

    # 9 - paste plain text, the clipboard restored after
    $saved = Get-Clipboard -Raw
    try {
        Set-Clipboard -Value "pasted line one`r`npasted line two"
        Send-ToNoto @(0x11) 0x23 300
        Send-ToNoto @(0x11) 0x56 700
        Add-Result $run '9  Ctrl+V pastes plain text with LF line breaks' ((Editor-Value).EndsWith("pasted line one`npasted line two")) "tail='$(((Editor-Value).Substring([Math]::Max(0, (Editor-Value).Length - 32))) -replace "`n", '\n')'"
    }
    finally { if ($null -ne $saved) { Set-Clipboard -Value $saved } else { Set-Clipboard -Value $null } }

    # 10 - autosave, leave, rendered
    $value = Editor-Value
    Start-Sleep -Milliseconds 1700
    $saved10 = (Stored $Meeting) -eq $value
    Send-ToNoto @() 0x1B 900
    $read = Read-Text (Card 'Weekly sync')
    Add-Result $run '10 a second without typing writes the note; Escape leaves; the card renders it' ($saved10 -and $read -match 'great' -and $read -notmatch '\*\*great\*\*') "saved=$saved10"

    # 11 - close and reopen
    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
    $afterClose = Stored $Meeting
    Start-Fresh 'Dark'
    Edit-Card 'Weekly sync'
    Add-Result $run '11 closed and opened again: the same source in the database and in the editor' ($afterClose -eq $value -and (Editor-Value) -eq $value) "db=$($afterClose -eq $value) editor=$((Editor-Value) -eq $value)"

    # 12 - Dark
    $darkEdit = Shot 'dark-editing'
    Send-ToNoto @() 0x1B 900
    $darkStack = Shot 'dark-stack'
    $darkRead = Read-Text (Card 'Groceries')
    Add-Result $run '12 Dark: cards rendered and the editor styled the same way (see the screenshots)' ($darkRead -match '☐' -and $darkRead -notmatch '\[ \]') "screenshots: $lightStack $lightEdit $darkStack $darkEdit"

    # 13 - cost on realistic notes
    # The long notes follow the three short ones; Ctrl+Alt+Down (G12) edits the next card, scrolling it into view, so
    # a card far down the virtualised stack is reached the way a user reaches it.
    Edit-Card 'A plain note with no markup at all.'
    foreach ($size in $Sizes.Keys) {
        $mark = @(Traces 'Noto\.Editor').Count
        Send-ToNoto @(0x11, 0x12) 0x28 300
        # A long card takes a moment to scroll into view and realise its editor: wait for Noto's own load trace.
        $deadline = (Get-Date).AddSeconds(15); $load = $null
        while (-not $load -and (Get-Date) -lt $deadline) { $load = @(Traces 'Noto\.Editor' | Select-Object -Skip $mark | Where-Object { $_ -match 'Noto\.Editor loaded' })[-1]; if (-not $load) { Start-Sleep -Milliseconds 200 } }
        Start-Sleep -Milliseconds 500
        if (-not $load) { $null = Shot "no-load-$($size -replace ' ', '')"; Write-Host "focus=$($AE::FocusedElement.Current.AutomationId)|$($AE::FocusedElement.Current.ControlType.ProgrammaticName)|$($AE::FocusedElement.Current.Name)" }
        $rendered = @(Traces 'Noto\.Editor' | Select-Object -Skip $mark | Where-Object { $_ -match 'Noto\.Editor rendered' })[-1]
        Send-ToNoto @(0x11) 0x24 300
        Start-Sleep -Milliseconds 1000
        # One key at a time, each waited for: Noto's own trace of the work it does per keystroke (read back, parse,
        # restyle what changed).
        $changes = @()
        foreach ($ch in 'abcde'.ToCharArray()) {
            $before = @(Traces 'Noto\.Editor change').Count
            Type-Text $ch
            $deadline = (Get-Date).AddSeconds(5)
            while (@(Traces 'Noto\.Editor change').Count -le $before -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 10 }
            $line = @(Traces 'Noto\.Editor change')[-1]
            $changes += Ms $line
        }
        $title = (Editor-Value).Split("`n")[0]
        Add-Result $run "13 $size realistic note: load and keystroke cost recorded" ($null -ne $load -and $title -eq "abcdeLong note $size") "chars=$((Editor-Value).Length) load: $($load -replace '^.*Noto\.Editor ', '') | first frame: $(Ms $rendered) ms | per keystroke ms: $($changes -join ', ') | title='$title'"
    }
}) }
finally {
    $script:log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($script:log.All())
}
