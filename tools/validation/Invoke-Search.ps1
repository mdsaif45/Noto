#Requires -Version 7.4
<#
.SYNOPSIS
  Search (ADR-018): Ctrl+F from every surface, results as you type, the keyboard in the box, opening a note with the
  caret at its match, opening a folder, scope, empty and no-results states, quick copy, and the cost per keystroke.

.DESCRIPTION
  A fresh isolated data root seeded with three small folders of realistic notes and an Archive of 3,000 more. Keys
  reach Noto only while it is verifiably the foreground window (the activation hotkey arranges that). Typing is real
  key input. The clipboard is used for case 9 and restored afterwards.

     1  Ctrl+F on the folder list opens Search with the keyboard in its box and a hint
     2  typing "meet" shows results as it goes: the note whose title matches first, the count announced
     3  Down chooses a result and Enter opens it: Search closes, the note is edited, the caret at its first match
     4  Ctrl+F in the editor saves what was typed; Escape returns to the editor, still editing the same text
     5  a folder whose name matches is offered first; Enter enters it
     6  inside a folder, Search can be narrowed to it
     7  an empty box shows the hint; a query that matches nothing says so; neither lists anything
     8  an apostrophe and FTS5 syntax are only text: "it's" finds its note, the rest finds nothing and breaks nothing
     9  Ctrl+C copies the chosen note and hides Noto; Ctrl+Shift+C copies it without its title line
    10  cost: Noto's own trace for each keystroke of a query over 3,000 notes
    11  Dark: the same results, highlighted (screenshots of both themes in the run)

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Search.ps1 -NotoExe <path>\Noto.exe
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

$run = New-ValidationRun 'search'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$TP = [System.Windows.Automation.TextPattern]
$Work = '01J0000000000000000000F031'; $HomeFolder = '01J0000000000000000000F032'; $Groceries = '01J0000000000000000000F033'; $Archive = '01J0000000000000000000F034'
$Sync = '01J00000000000000000000S01'
$SyncText = "## Weekly sync`n`n**Attendees:** Priya, Tom, me`n`nWe need to meet the vendor about the café budget."
$ShoppingText = "Shopping list`n`n- [ ] milk`n- [x] bread"
$Notes = @(
    @($Sync, $Work, $SyncText),
    @('01J00000000000000000000S02', $Work, "Project plan`n`nMilestones, owners and the release date."),
    @('01J00000000000000000000S03', $Work, "It's done`n`nThe apostrophe note."),
    @('01J00000000000000000000S04', $HomeFolder, "Meeting the neighbours`n`nBring a cake on Saturday."),
    @('01J00000000000000000000S05', $HomeFolder, "Recipes`n`nPancakes: eggs, milk, flour."),
    @('01J00000000000000000000S06', $Groceries, $ShoppingText)
)

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
function Seed-Archive([int] $Count, [string] $Now) {
    <# One transaction: the index is kept by Noto's own triggers, as for any write. #>
    $db = Join-Path $script:data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $tx = $c.BeginTransaction()
        $cmd = $c.CreateCommand(); $cmd.Transaction = $tx
        $cmd.CommandText = 'INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES ($n, $f, $c, $s, $t, $t)'
        $words = 'alpha', 'project', 'plan', 'invoice', 'release', 'budget', 'design', 'review', 'travel', 'garden'
        for ($i = 0; $i -lt $Count; $i++) {
            $cmd.Parameters.Clear()
            [void]$cmd.Parameters.AddWithValue('$n', ('01J0000000000000000A{0:D6}' -f $i))
            [void]$cmd.Parameters.AddWithValue('$f', $Archive)
            $w = $words[$i % 10]; $v = $words[($i * 3 + 1) % 10]
            [void]$cmd.Parameters.AddWithValue('$c', "Archive $w $i`n`nNotes on the $v and the $w for 2026. Priya and Tom agreed.`n`n- [ ] follow up on $v`n`n" + ("Plain paragraph text about $w that goes on for a while. " * 8))
            [void]$cmd.Parameters.AddWithValue('$s', $i)
            [void]$cmd.Parameters.AddWithValue('$t', $Now)
            [void]$cmd.ExecuteNonQuery()
        }
        $tx.Commit()
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
function Is-Open([string] $Id) { $e = By-Id $Id; [bool]($e -and -not $e.Current.IsOffscreen) }
function Focused-Id { $AE::FocusedElement.Current.AutomationId }
function Editor { Find-ById 'NoteEditor' }
function Editor-Value { (Editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Caret-Offset {
    <# Characters from the start of the source to the caret, as the editor's Text pattern counts them. #>
    $tp = (Editor).GetCurrentPattern($TP::Pattern)
    $before = $tp.DocumentRange.Clone()
    $before.MoveEndpointByRange([System.Windows.Automation.Text.TextPatternRangeEndpoint]::End, @($tp.GetSelection())[0], [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start)
    $before.GetText(-1).Length
}
function Results { @((Find-ById 'ResultList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) }
function Result-Names { @(Results | ForEach-Object { $_.Current.Name }) }
function Selected-Name { @(Results | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } | ForEach-Object { $_.Current.Name })[0] }
function Status { (Find-ById 'SearchStatus').Current.Name }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 500) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Type-Text([string] $Text, [int] $Pause = 40) {
    <# Real key input, one Unicode character at a time (KEYEVENTF_UNICODE), only while Noto is in front. #>
    foreach ($ch in $Text.ToCharArray()) {
        if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to type: Noto is not the foreground window' }
        $down = New-Object NotoVal.Win32+INPUT; $down.type = 1; $down.wScan = [uint16]$ch; $down.dwFlags = 4
        $up = New-Object NotoVal.Win32+INPUT; $up.type = 1; $up.wScan = [uint16]$ch; $up.dwFlags = 6
        [NotoVal.Win32]::Keys(@($down, $up)); Start-Sleep -Milliseconds $Pause
    }
    Start-Sleep -Milliseconds 400
}
function Clear-Box { Send-ToNoto @(0x11) 0x41 150; Send-ToNoto @() 0x08 400 }
function Open-Search {
    Send-ToNoto @(0x11) 0x46 900
    $null = Find-ById 'SearchBox'
}
function Bring-Forward {
    $null = Set-ForegroundByKeyboard $script:target
    Send-ActivationChord -AllowedForeground $script:target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}
function Shot([string] $Name) {
    $r = New-Object NotoVal.Capture+RECT; [void][NotoVal.Capture]::GetWindowRect($script:noto.Hwnd, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
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
    $folder = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $folder.SetFocus(); Start-Sleep -Milliseconds 400
}

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'Search (ADR-018)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $order = 0
    foreach ($folder in @(@($Work, 'Work'), @($HomeFolder, 'Home'), @($Groceries, 'Groceries'), @($Archive, 'Archive'))) {
        $order++
        $null = Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, `$name, `$s, `$t, `$t)" @{ '$f' = $folder[0]; '$name' = $folder[1]; '$s' = $order; '$t' = $now }
    }
    foreach ($note in $Notes) {
        $order++
        $null = Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" @{ '$n' = $note[0]; '$f' = $note[1]; '$c' = $note[2]; '$s' = $order; '$t' = $now }
    }
    Seed-Archive 3000 $now

    Start-Fresh 'Light'

    # 1 - Ctrl+F on the folder list
    Open-Search
    Add-Result $run '1  Ctrl+F on the folder list opens Search, the keyboard in its box, with a hint' ((Focused-Id) -eq 'SearchBox' -and (Status) -match 'Type to search' -and (Results).Count -eq 0) "focus=$(Focused-Id) status='$(Status)'"

    # 2 - results as you type
    $mark = @(Traces 'Noto\.Search results').Count
    Type-Text 'meet'
    $typed = @(Traces 'Noto\.Search results' | Select-Object -Skip $mark)
    $names = @(Result-Names)
    $lightShot = Shot 'light-results'
    Add-Result $run '2  typing shows results as it goes: the title match first, the count announced' ($typed.Count -ge 4 -and $names.Count -eq 2 -and $names[0] -like 'Meeting the neighbours*' -and $names[1] -like 'Weekly sync*' -and (Status) -eq '2 results') "per-keystroke traces=$($typed.Count) results=$($names -join ' | ') status='$(Status)' screenshot: $lightShot"

    # 3 - Down, Enter: the note opens with the caret at its match
    Send-ToNoto @() 0x28 400
    $chosen = Selected-Name
    Send-ToNoto @() 0x0D 1500
    $offset = Caret-Offset
    $expected = $SyncText.IndexOf('meet', [StringComparison]::Ordinal)
    Add-Result $run '3  Down chooses, Enter opens: Search closed, the note edited, the caret at the first match' ((-not (Is-Open 'SearchRoot')) -and $chosen -like 'Weekly sync*' -and (Editor-Value) -eq $SyncText -and $offset -eq $expected -and (Focused-Id) -eq 'NoteEditor') "chosen='$chosen' caret=$offset expected=$expected focus=$(Focused-Id)"

    # 4 - from the editor: saved, then back, still editing
    Send-ToNoto @(0x11) 0x23 300
    Type-Text ' today'
    Open-Search
    $saved = Stored $Sync
    Send-ToNoto @() 0x1B 900
    $value = Editor-Value
    Add-Result $run '4  Ctrl+F in the editor saves what was typed; Escape returns to the editor, still editing it' ($saved -eq $value -and $value.EndsWith('budget. today') -and (Focused-Id) -eq 'NoteEditor' -and -not (Is-Open 'SearchRoot')) "saved=$($saved -eq $value) focus=$(Focused-Id) value='$($value -replace "`n", '
')'"
    Send-ToNoto @() 0x1B 900

    # 5 - a folder result
    Open-Search
    Type-Text 'groc'
    $names = @(Result-Names)
    Send-ToNoto @() 0x0D 1500
    $cards = @((Find-ById 'NoteList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)) | ForEach-Object { $_.Current.Name })
    Add-Result $run '5  a folder whose name matches comes first; Enter enters it' ($names[0] -eq 'Groceries, Folder' -and $cards.Count -eq 1 -and $cards[0] -like 'Shopping list*' -and -not (Is-Open 'SearchRoot')) "results=$($names -join ' | ') cards=$($cards -join ' | ')"

    # 6 - narrowed to the open folder
    Open-Search
    Type-Text 'milk'
    $all = @(Result-Names)
    $toggle = Find-ById 'ScopeToggle'
    $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 600
    $narrow = @(Result-Names)
    Add-Result $run '6  inside a folder, Search narrows to it' ($toggle.Current.Name -eq 'Only in Groceries' -and $all.Count -eq 2 -and $narrow.Count -eq 1 -and $narrow[0] -like 'Shopping list*') "toggle='$($toggle.Current.Name)' all=$($all -join ' | ') narrowed=$($narrow -join ' | ')"
    $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 400

    # 7 - empty and no results
    Clear-Box
    $hint = Status; $emptyCount = (Results).Count
    Type-Text 'zzqx'
    $none = Status; $noneCount = (Results).Count
    Add-Result $run '7  an empty box shows the hint; nothing matching says so; neither lists anything' ($hint -match 'Type to search' -and $emptyCount -eq 0 -and $none -eq 'No notes or folders match.' -and $noneCount -eq 0) "empty='$hint' none='$none'"

    # 8 - apostrophes and FTS5 syntax are text
    Clear-Box
    Type-Text "it's"
    $apostrophe = @(Result-Names)
    Clear-Box
    Type-Text '"NEAR(a* AND -b) ^c:d'
    $syntax = Status
    Add-Result $run '8  "it''s" finds its note; FTS5 syntax finds nothing and breaks nothing' ($apostrophe.Count -eq 1 -and $apostrophe[0] -like "It's done*" -and $syntax -eq 'No notes or folders match.' -and (Is-Open 'SearchBox')) "apostrophe=$($apostrophe -join ' | ') syntax='$syntax'"

    # 9 - quick copy, then copy without the title
    $savedClip = Get-Clipboard -Raw
    try {
        Clear-Box
        Type-Text 'shopping'
        Send-ToNoto @(0x11) 0x43 1200
        $copied = (Get-Clipboard -Raw) -replace "`r`n", "`n"
        $hidden = -not [NotoVal.Win32]::IsWindowVisible($script:noto.Hwnd)
        Bring-Forward
        Open-Search
        Clear-Box
        Type-Text 'shopping'
        Send-ToNoto @(0x11, 0x10) 0x43 1200
        $rest = (Get-Clipboard -Raw) -replace "`r`n", "`n"
        Add-Result $run '9  Ctrl+C copies the chosen note and hides Noto; Ctrl+Shift+C copies it without its title' ($copied -eq $ShoppingText -and $hidden -and $rest -eq "- [ ] milk`n- [x] bread") "copied=$($copied -eq $ShoppingText) hidden=$hidden rest='$($rest -replace "`n", '\n')'"
        Bring-Forward
    }
    finally { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } else { Set-Clipboard -Value $null } }

    # 10 - cost per keystroke over 3,000 notes
    Open-Search
    Clear-Box
    $mark = @(Traces 'Noto\.Search results').Count
    Type-Text 'alpha plan' 150
    $costs = @(Traces 'Noto\.Search results' | Select-Object -Skip $mark | ForEach-Object { Ms $_ })
    $worst = ($costs | Measure-Object -Maximum).Maximum
    Add-Result $run '10 3,000 notes: every keystroke answered within the 100 ms budget' ($costs.Count -ge 9 -and $worst -lt 100) "per keystroke ms: $($costs -join ', ')"
    Send-ToNoto @() 0x1B 900

    # 11 - Dark
    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
    Start-Fresh 'Dark'
    Open-Search
    Type-Text 'meet'
    $darkShot = Shot 'dark-results'
    Add-Result $run '11 Dark: the same results, highlighted (see the screenshots)' ((Result-Names).Count -eq 2) "screenshots: $lightShot $darkShot"
}) }
finally {
    $script:log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($script:log.All())
}
