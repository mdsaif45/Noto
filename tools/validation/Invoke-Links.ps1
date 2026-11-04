#Requires -Version 7.4
<#
.SYNOPSIS
  Links (ADR-019 §1-§3): a card's link opens on a click; in the editor Ctrl+Enter and Ctrl+Click open the link there;
  a program asks first; a target that is gone or a refused link says so; bare addresses are links; a web address
  pasted over a selection links it; copied files and folders paste as links.

.DESCRIPTION
  A fresh isolated data root with one folder holding one note of links. Every local target lives under the run's own
  directory: a folder (opened in Explorer, whose window is closed again), a script (never run: the case cancels its
  confirmation) and a path that does not exist. No web address is ever opened: no browser is started. Keys and clicks
  reach Noto only while it is verifiably the foreground window. The clipboard is used for cases 8 and 9 and restored.

     1  a card's link is a hyperlink; invoking it opens its folder (Noto's own trace; the Explorer window appears)
     2  in the editor, Ctrl+Enter with the caret in the link opens it
     3  in the editor, Ctrl+Click on the link opens it; a plain click only places the caret
     4  a link to a folder that is gone says so and opens nothing
     5  a link to a script asks first, Cancel the default; cancelling opens nothing
     6  a refused link (javascript:) says so and opens nothing
     7  a bare address in the text is styled as a link (underlined in the editor)
     8  a web address pasted over a selected word makes that word its link
     9  a copied folder and file paste as links to them

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Links.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'links'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$TP = [System.Windows.Automation.TextPattern]
$FolderId = '01J0000000000000000000F041'; $NoteId = '01J00000000000000000000N01'

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
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Editor { Find-ById 'NoteEditor' }
function Editor-Value { (Editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Range-Of([string] $Needle) { (Editor).GetCurrentPattern($TP::Pattern).DocumentRange.FindText($Needle, $false, $false) }
function Notice { $n = By-Id 'NoteNoticeText'; if ($n -and -not $n.Current.IsOffscreen) { $n.Current.Name } else { '' } }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 500) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Click-At([int] $X, [int] $Y, [switch] $Ctrl) {
    <# A real left click, only while Noto is in front and only where the window under the point is Noto. #>
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to click: Noto is not the foreground window' }
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $X; $pt.Y = $Y
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $script:noto.Hwnd) { throw "refusing to click at ($X,$Y): it is not Noto" }
    [void][NotoVal.Win32]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 150
    if ($Ctrl) { [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x11, $false))) }
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60
    [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 100
    if ($Ctrl) { [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk(0x11, $true))) }
    Start-Sleep -Milliseconds 700
}
function Centre-Of($Range) { $r = @($Range.GetBoundingRectangles())[0]; @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) }
function Bring-Forward {
    <# A window another app just opened (Explorer) holds the foreground for a moment: a few attempts, each verified. #>
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $null = Set-ForegroundByKeyboard $script:target
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:target.Hwnd) { Send-ActivationChord -AllowedForeground $script:target.Hwnd }
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:noto.Hwnd) { return }
        Start-Sleep -Milliseconds 1000
    }
    $fg = [NotoVal.Win32]::GetForegroundWindow(); $owner = [uint32]0; [void][NotoVal.Win32]::GetWindowThreadProcessId($fg, [ref]$owner)
    $title = New-Object System.Text.StringBuilder 256; [void][NotoVal.Win32]::GetWindowText($fg, $title, 256)
    throw "the hotkey did not bring Noto forward (foreground: $((Get-Process -Id $owner -ErrorAction SilentlyContinue).ProcessName) '$title')"
}
function Set-FileClipboard([string[]] $Paths) {
    <#
      Files and folders on the clipboard as Explorer's Copy puts them (CF_HDROP), through Windows PowerShell, which is
      STA. Retried while another program holds the clipboard, and read back: measured, a set that lost that race left
      the clipboard empty and case 9 failed as if the paste had; now the run stops, naming the clipboard instead.
    #>
    $file = Join-Path $run.Root 'file-clipboard.ps1'
    Set-Content $file "Add-Type -AssemblyName System.Windows.Forms; `$c = New-Object System.Collections.Specialized.StringCollection; foreach (`$p in `$args) { [void]`$c.Add(`$p) }; `$d = New-Object System.Windows.Forms.DataObject; `$d.SetFileDropList(`$c); [System.Windows.Forms.Clipboard]::SetDataObject(`$d, `$true, 10, 100); if (-not [System.Windows.Forms.Clipboard]::ContainsFileDropList()) { exit 1 }"
    & powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File $file @Paths
    if ($LASTEXITCODE -ne 0) { throw 'the clipboard did not take the files (another program held it)' }
}
function Traces([string] $Pattern) { @($script:log.Lines($script:noto.Pid) | Where-Object { $_ -match $Pattern }) }
function Opened-Since([int] $Mark) { @(Traces 'Noto\.Link open' | Select-Object -Skip $Mark) }
function Close-Explorer([string] $Folder) {
    <# The Explorer windows showing this run's own folder: explorer.exe's top-level windows titled with its name, closed. #>
    $name = Split-Path $Folder -Leaf
    $explorer = @(Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
    $found = [System.Collections.Generic.List[IntPtr]]::new()
    $callback = [NotoVal.Win32+EnumProc] {
        param($h, $l)
        $title = New-Object System.Text.StringBuilder 256
        [void][NotoVal.Win32]::GetWindowText($h, $title, 256)
        $owner = [uint32]0; [void][NotoVal.Win32]::GetWindowThreadProcessId($h, [ref]$owner)
        if (($title.ToString() -eq $name -or $title.ToString().StartsWith("$name - ") -or $title.ToString() -eq $Folder) -and $explorer -contains $owner -and [NotoVal.Win32]::IsWindowVisible($h)) { $found.Add($h) }
        $true
    }
    $deadline = (Get-Date).AddSeconds(5)
    while ($found.Count -eq 0 -and (Get-Date) -lt $deadline) { [void][NotoVal.Win32]::EnumWindows($callback, [IntPtr]::Zero); if ($found.Count -eq 0) { Start-Sleep -Milliseconds 200 } }
    foreach ($h in $found) { [void][NotoVal.Win32]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 1500   # the foreground settles after Explorer's window goes
    $found.Count
}
function Card-Hyperlink([string] $Name) {
    $cards = (Find-ById 'NoteList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Hyperlink))
    @($cards | Where-Object { $_.Current.Name -eq $Name })[0]
}
function Edit-Note {
    $card = (Find-ById 'NoteList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x0D 900
    $null = Editor
}
function Caret-In([string] $Needle) { $r = Range-Of $Needle; $r.Move([System.Windows.Automation.Text.TextUnit]::Character, 2) | Out-Null; $r.Select(); Start-Sleep -Milliseconds 300 }

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'Links (ADR-019)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    # A name only this run uses: Close-Explorer matches windows by it, and must never touch one of the user's.
    $linked = Join-Path $run.Root "noto-linked-$($run.Id)"; New-Item -ItemType Directory $linked | Out-Null
    $scriptFile = Join-Path $run.Root 'run.cmd'; Set-Content $scriptFile 'exit /b 0'
    $paper = Join-Path $run.Root 'paper.txt'; Set-Content $paper 'a document'
    $gone = Join-Path $run.Root 'gone'
    $uri = { param($p) ([Uri]$p).AbsoluteUri }
    $text = "Links`n`n[Project folder]($(& $uri $linked))`n[Gone]($(& $uri $gone))`n[Script]($(& $uri $scriptFile))`n[Bad](javascript:alert(1))`nSee https://example.com/spec here`nread the spec today"

    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Links', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }
    $null = Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = $text; '$t' = $now }

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward
    $folder = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $folder.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x28 1000

    # 1 - a card's link
    $mark = (Traces 'Noto\.Link open').Count
    $hyperlink = Card-Hyperlink 'Project folder'
    $hyperlink.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1500
    $opened = @(Opened-Since $mark)
    $closed = Close-Explorer $linked
    Bring-Forward
    Add-Result $run '1  a card''s link is a hyperlink; invoking it opens its folder' ($null -ne $hyperlink -and $opened.Count -eq 1 -and $opened[0] -match 'kind=Local folder=True outcome=Opened' -and $closed -ge 1) "trace='$($opened -join ' | ')' explorer windows closed=$closed"

    # 2 - Ctrl+Enter in the editor
    Edit-Note
    Caret-In 'Project folder'
    $mark = (Traces 'Noto\.Link open').Count
    Send-ToNoto @(0x11) 0x0D 1500
    $opened = @(Opened-Since $mark)
    $closed = Close-Explorer $linked
    Bring-Forward
    $unchanged = (Editor-Value) -eq $text
    Add-Result $run '2  in the editor, Ctrl+Enter in a link opens it (and types nothing)' ($opened.Count -eq 1 -and $opened[0] -match 'outcome=Opened' -and $closed -ge 1 -and $unchanged) "trace='$($opened -join ' | ')' unchanged=$unchanged"

    # 3 - Ctrl+Click; a plain click only places the caret
    $at = Centre-Of (Range-Of 'Project folder')
    $mark = (Traces 'Noto\.Link open').Count
    Click-At $at[0] $at[1]
    $plain = @(Opened-Since $mark).Count
    Click-At $at[0] $at[1] -Ctrl
    Start-Sleep -Milliseconds 800
    $opened = @(Opened-Since $mark)
    $closed = Close-Explorer $linked
    Bring-Forward
    Add-Result $run '3  Ctrl+Click on a link opens it; a plain click only places the caret' ($plain -eq 0 -and $opened.Count -eq 1 -and $closed -ge 1) "plain-click opens=$plain trace='$($opened -join ' | ')'"

    # 4 - gone
    Caret-In 'Gone'
    $mark = (Traces 'Noto\.Link open').Count
    Send-ToNoto @(0x11) 0x0D 900
    Add-Result $run '4  a link to a folder that is gone says so and opens nothing' ((Notice) -eq 'That file or folder is no longer there.' -and @(Opened-Since $mark).Count -eq 0) "notice='$(Notice)'"

    # 5 - a script asks first; Cancel
    Caret-In 'Script'
    $mark = (Traces 'Noto\.Link open').Count
    Send-ToNoto @(0x11) 0x0D 1200
    $dialog = $AE::RootElement.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Open run.cmd?'))
    if (-not $dialog) { $dialog = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Open run.cmd?')) }
    $focusName = $AE::FocusedElement.Current.Name
    Send-ToNoto @() 0x1B 900
    $after = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Open run.cmd?'))
    Add-Result $run '5  a link to a script asks first, Cancel the default; cancelling opens nothing' ($null -ne $dialog -and $focusName -eq 'Cancel' -and $null -eq $after -and @(Opened-Since $mark).Count -eq 0) "dialog=$($null -ne $dialog) focus='$focusName' after=$($null -ne $after)"

    # 6 - refused
    Bring-Forward
    $null = Editor
    Caret-In 'Bad'
    $mark = (Traces 'Noto\.Link open').Count
    Send-ToNoto @(0x11) 0x0D 900
    Add-Result $run '6  a refused link (javascript:) says so and opens nothing' ((Notice) -eq "Noto doesn't open this kind of link." -and @(Opened-Since $mark).Count -eq 0) "notice='$(Notice)'"

    # 7 - a bare address is a link
    $bare = Range-Of 'https://example.com/spec'
    $underline = $bare.GetAttributeValue($TP::UnderlineStyleAttribute)
    Add-Result $run '7  a bare address in the text is styled as a link' ("$underline" -ne 'None' -and "$underline" -ne '0') "underline=$underline"

    # 8 - a web address pasted over a selection; 9 - copied files and folders
    $savedClip = Get-Clipboard -Raw
    try {
        (Range-Of 'spec today').Select(); Start-Sleep -Milliseconds 200
        $word = Range-Of 'spec today'; [void]$word.MoveEndpointByUnit([System.Windows.Automation.Text.TextPatternRangeEndpoint]::End, [System.Windows.Automation.Text.TextUnit]::Character, -6); $word.Select(); Start-Sleep -Milliseconds 300
        Set-Clipboard -Value 'https://example.com/the-spec'
        Send-ToNoto @(0x11) 0x56 900
        $linkedWord = (Editor-Value).Contains('read the [spec](https://example.com/the-spec) today')
        Add-Result $run '8  a web address pasted over a selected word makes the word its link' $linkedWord "tail='$(((Editor-Value).Split("`n"))[-1])'"

        Send-ToNoto @(0x11) 0x23 300
        Send-ToNoto @() 0x0D 300
        Set-FileClipboard $linked, $paper
        Send-ToNoto @(0x11) 0x56 1200
        $lines = (Editor-Value).Split("`n")
        $pasted = $lines[-2..-1] -join ' | '
        Add-Result $run '9  a copied folder and file paste as links to them' ($lines[-2] -eq "[$(Split-Path $linked -Leaf)]($(& $uri $linked))" -and $lines[-1] -eq "[paper.txt]($(& $uri $paper))") "pasted='$pasted'"
    }
    finally { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } else { Set-Clipboard -Value $null } }

    Send-ToNoto @() 0x1B 900
    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
}) }
finally {
    $script:log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($script:log.All())
}
