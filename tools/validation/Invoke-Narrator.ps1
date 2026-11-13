#Requires -Version 7.4
<#
.SYNOPSIS
  #161 section 1 (Narrator), by Narrator itself: the phrase it speaks after each step on Noto's surfaces, read back with
  its own "copy last spoken phrase" (Narrator key + Ctrl + X), never inferred from UI Automation properties alone.

.DESCRIPTION
  One isolated data root on a temporary virtual desktop; Noto kept shown while Narrator takes activation (its hide on
  deactivation off for this run). Narrator is started for the run (Narrator.exe, the app; no setting is changed) and
  stopped before the run leaves its desktop, even on failure. With Narrator on, a click no longer hands the foreground
  over (measured), so focus goes back to Noto through UI Automation. Keys reach Noto only while it is the foreground
  window. The clipboard, which the copied phrase passes through, is restored after. Speech recap (Narrator key + Alt +
  X) was tried first: its text is inside a XAML island UI Automation does not reach.

    N1  the folder list: a row says its name, and "Pinned" for the pinned one
    N3  the card stack: a card says its title, and "Folded" with the lines it hides
    N4  card commands: "Unfolded" and "Folded" are announced
    N5  the editor: "Note content"
    N9  Search: the count, "1 result"
    N10 Settings: a control says its name and its state

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Narrator.ps1 -NotoExe <frozen Release>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'narrator'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$Work = '01J0000000000000000000F0B1'; $HomeFolder = '01J0000000000000000000F0B2'

function Sql([string] $Text, [hashtable] $Parameters = @{}) {
    $db = Join-Path $script:data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $Text; foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }; $cmd.ExecuteNonQuery() }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Rows([string] $List) { @((Find-ById $List).FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))) }
function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Until([scriptblock] $Condition, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 250 }
    [bool](& $Condition)
}
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 1500) {
    if ((Fg) -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Type-Text([string] $Text) {
    foreach ($ch in $Text.ToCharArray()) {
        if ((Fg) -ne $script:noto.Hwnd) { throw 'refusing to type: Noto is not the foreground window' }
        $down = New-Object NotoVal.Win32+INPUT; $down.type = 1; $down.wScan = [uint16]$ch; $down.dwFlags = 4
        $up = New-Object NotoVal.Win32+INPUT; $up.type = 1; $up.wScan = [uint16]$ch; $up.dwFlags = 6
        [NotoVal.Win32]::Keys(@($down, $up)); Start-Sleep -Milliseconds 80
    }
}
function Bring-Forward {
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $null = Set-ForegroundByKeyboard $script:target
        if ((Fg) -eq $script:target.Hwnd) { Send-ActivationChord -AllowedForeground $script:target.Hwnd }
        if ((Fg) -eq $script:noto.Hwnd) { return }
        Start-Sleep -Milliseconds 1000
    }
    throw 'the hotkey did not bring Noto forward'
}
function Focus-On($Element) {
    <# Focus through UI Automation, which also brings Noto's window forward; Narrator speaks the focused element. #>
    $Element.SetFocus()
    if (-not (Until { (Fg) -eq $script:noto.Hwnd } 5)) { throw 'Noto did not come forward' }
    Start-Sleep -Milliseconds 2500
}
function Narrator-Running { [bool](Get-Process Narrator -ErrorAction SilentlyContinue) }
function Spoken {
    <# Narrator's last spoken phrase: Narrator key (Insert) + Ctrl + X copies it to the clipboard. #>
    Set-Clipboard -Value 'nothing copied'
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x2D, $false), [NotoVal.Win32]::Vk(0x11, $false), [NotoVal.Win32]::Vk(0x58, $false), [NotoVal.Win32]::Vk(0x58, $true), [NotoVal.Win32]::Vk(0x11, $true), [NotoVal.Win32]::Vk(0x2D, $true)))
    Start-Sleep -Milliseconds 1200
    [string](Get-Clipboard -Raw)
}

$saved = Get-Clipboard -Raw
try {
    exit (Invoke-IsolatedRun $run -Title '#161 section 1: Narrator' -Body {
      try {
        Assert-ChordFree $run
        if (Narrator-Running) { throw 'Narrator is already running: the run starts and stops its own' }
        $script:target = Start-TargetApp $run
        $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
        if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Sql "INSERT INTO Folders (Id, Name, SortOrder, IsPinned, CreatedAt, UpdatedAt) VALUES (`$f, 'Work', 1, 1, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }
        $null = Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Home', 2, `$t, `$t)" @{ '$f' = $HomeFolder; '$t' = $now }
        $null = Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsFolded, CreatedAt, UpdatedAt) VALUES ('01J00000000000000000000B01', `$f, `"# Weekly sync`n**Bold** words`n- [ ] call`", 1, 0, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }
        $null = Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsFolded, CreatedAt, UpdatedAt) VALUES ('01J00000000000000000000B02', `$f, `"Folded note`nhidden line`", 2, 1, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $script:data -Key 'workspace.hide-on-deactivation' -Value 'false'

        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
        Bring-Forward
        Start-Process narrator.exe
        if (-not (Until { Narrator-Running } 15)) { Add-Invalid $run 'Narrator did not start'; return }
        Start-Sleep -Seconds 8   # its start-up speech

        # N1 - the folder list
        $rows = Rows 'FolderList'
        Focus-On $rows[1]; $homeSaid = Spoken
        Focus-On $rows[0]; $work = Spoken
        Start-Sleep -Seconds 5; $workLater = Spoken   # a hint (help text) comes after a pause, as its own phrase
        Add-Result $run 'N1 folder list: a row says its name, Pinned for the pinned one' ($work -match 'Work' -and "$work $workLater" -match 'Pinned' -and $homeSaid -match 'Home') "Work: '$work' then '$workLater' | Home: '$homeSaid'"

        # N3 - the card stack
        Focus-On (Rows 'FolderList')[0]
        Send-ToNoto @(0x11) 0x28 2500   # Ctrl+Down: into Work
        $cards = Rows 'NoteList'
        Focus-On $cards[0]; $firstCard = Spoken
        Focus-On $cards[1]; $folded = Spoken
        Start-Sleep -Seconds 5; $foldedLater = Spoken
        Add-Result $run 'N3 card stack: a card says its title; a folded one says Folded and what it hides' ($firstCard -match 'Weekly sync' -and $folded -match 'Folded note' -and "$folded $foldedLater" -match 'Folded, 1 line hidden') "first: '$firstCard' | folded: '$folded' then '$foldedLater'"

        # N4 - commands announced
        Send-ToNoto @(0x11, 0x12) 0x27 2000; $unfolded = Spoken   # Ctrl+Alt+Right
        Focus-On (Rows 'NoteList')[1]
        Send-ToNoto @(0x11, 0x12) 0x25 2000; $foldedAgain = Spoken   # Ctrl+Alt+Left
        Add-Result $run 'N4 card commands announced: Unfolded, Folded' ($unfolded -match 'Unfolded' -and $foldedAgain -match 'Folded') "unfold: '$unfolded' | fold: '$foldedAgain'"

        # N5 - the editor
        Focus-On (Rows 'NoteList')[0]
        Send-ToNoto @() 0x0D 3000; $editor = Spoken   # Enter: edit Weekly sync
        Add-Result $run 'N5 the editor says "Note content"' ($editor -match 'Note content') "said: '$editor'"
        Send-ToNoto @() 0x1B 1500

        # N9 - Search
        Send-ToNoto @(0x11) 0x46 2500   # Ctrl+F
        # One key, so the status changes on the last key typed: its announcement, if any, is the last thing said.
        Type-Text 'q'
        Start-Sleep -Seconds 4; $search = Spoken
        Add-Result $run 'N9 Search announces what it found' ($search -match 'No notes or folders match') "said: '$search'"
        Send-ToNoto @() 0x1B 1500

        # N10 - Settings
        Send-ToNoto @(0x11) 0xBC 2500   # Ctrl+,
        Focus-On (Find-ById 'HideOnDeactivationSetting'); $toggle = Spoken
        Add-Result $run 'N10 Settings: a control says its name and its state' ($toggle -match 'Hide' -and $toggle -match '\b(On|Off|on|off)\b') "said: '$toggle'"
        Send-ToNoto @() 0x1B 1500
      }
      finally {
        # Stopped before the run leaves its desktop: a running Narrator holds the shell's desktop keys (measured).
        Get-Process Narrator -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
      }
    })
}
finally {
    Get-Process Narrator -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($null -ne $saved) { Set-Clipboard -Value $saved } else { Set-Clipboard -Value $null }
}
