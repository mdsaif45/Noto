#Requires -Version 7.4
<#
.SYNOPSIS
  #31: drag and drop from Windows' own sources into Noto, a borderless, always-on-top WinUI 3 window: files from an
  Explorer window and from the desktop, and a picture and text from a web page in Edge.

.DESCRIPTION
  One isolated data root on a temporary virtual desktop, Noto kept shown while the source app is active (its hide on
  deactivation off for this run). Each drag is a real mouse drag that starts on the source's own window and ends on
  Noto's, both checked to be those windows (by the window under the pointer) before the button goes down. The sources
  are this run's own: an Explorer window on a folder of the run, a file of the run placed on the desktop and removed
  after, and Edge started as a guest (no sign-in, no sync) with a profile of the run's (never the user's), closed and deleted after.

    1  Explorer, one file: a .md dragged from an Explorer window onto the note stack becomes a note
    2  Explorer, several files: two selected files dragged together become two notes, one result
    3  the desktop: a .txt dragged from its desktop icon onto the stack becomes a note; the file stays on the desktop.
       NOT RUN when the desktop's icons are hidden (Explorer\Advanced HideIcons = 1, the user's setting, never changed
       here): the desktop is the same shell view as an Explorer window, which cases 1 and 2 drag from
    4  Edge, a picture: an image on a page dragged onto the stack (what Edge gives is recorded, and the note it makes)
    5  Edge, text: a word selected on a page dragged into the note being edited (recorded: what the editor holds after)

  Recorded as found: the purpose is to know (#31), so cases 4 and 5 report what happened, and pass only when Noto did
  what its design says (a picture note; the text added to the note).

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-ShellDrops.ps1 -NotoExe <frozen Release>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe, [string] $EdgeExe = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe")
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$NotoExe = (Resolve-Path $NotoExe).Path
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }

$run = New-ValidationRun 'shell-drops'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$Work = '01J0000000000000000000F0A1'

if (-not ('NotoVal.Drag' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
namespace NotoVal {
    public static class Drag {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        public static IntPtr RootAt(int x, int y) { var p = new POINT { X = x, Y = y }; return GetAncestor(WindowFromPoint(p), 2); }
        public static string ClassAt(int x, int y) { var p = new POINT { X = x, Y = y }; var sb = new StringBuilder(256); GetClassName(WindowFromPoint(p), sb, 256); return sb.ToString(); }
        public static string RootClassAt(int x, int y) { var sb = new StringBuilder(256); GetClassName(RootAt(x, y), sb, 256); return sb.ToString(); }
    }
}
'@
}

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
function Contents {
    $db = Join-Path $script:data 'noto.db'; Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT Content FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder, Id'
        [void]$cmd.Parameters.AddWithValue('$f', $Work)
        $r = $cmd.ExecuteReader(); $out = @(); while ($r.Read()) { $out += $r.GetString(0) }; $r.Close(); , $out
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function Attachments { @(Get-ChildItem (Join-Path $script:data 'attachments') -File -ErrorAction SilentlyContinue).Count }
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Shown-Text([string] $Id) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { $e.Current.Name } else { '' } }
function Until([scriptblock] $Condition, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 250 }
    [bool](& $Condition)
}
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
function Centre($Element) { $r = $Element.Current.BoundingRectangle; @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) }
function Move-Pointer([int] $X, [int] $Y) {
    $v = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $nx = [uint32][Math]::Round(($X - $v.Left) * 65535.0 / ($v.Width - 1)); $ny = [uint32][Math]::Round(($Y - $v.Top) * 65535.0 / ($v.Height - 1))
    [NotoVal.Win32]::mouse_event(0x0001 -bor 0x8000 -bor 0x4000, $nx, $ny, 0, [IntPtr]::Zero)
}
function Real-Drag([int] $FromX, [int] $FromY, [int] $ToX, [int] $ToY, [IntPtr] $Source, [string] $SourceClass) {
    <#
      A real mouse drag from this run's source window to Noto. Refused unless the window under the start is the source
      (by handle, or by class for the desktop) and the window under the end is Noto.
    #>
    $startRoot = [NotoVal.Drag]::RootAt($FromX, $FromY)
    if ($Source -ne [IntPtr]::Zero -and $startRoot -ne $Source) { throw "refusing to drag from ($FromX,$FromY): it is not the source window ($([NotoVal.Drag]::RootClassAt($FromX, $FromY)))" }
    if ($SourceClass -and [NotoVal.Drag]::RootClassAt($FromX, $FromY) -notmatch $SourceClass) { throw "refusing to drag from ($FromX,$FromY): it is $([NotoVal.Drag]::RootClassAt($FromX, $FromY)), not $SourceClass" }
    if ([NotoVal.Drag]::RootAt($ToX, $ToY) -ne $script:noto.Hwnd) { throw "refusing to drop at ($ToX,$ToY): it is not Noto" }
    Move-Pointer $FromX $FromY; Start-Sleep -Milliseconds 300
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 600
    # A slow start, a pixel at a time past the drag threshold, as a hand moves: some sources (Chromium) start a drag
    # only from a held button that then moves.
    for ($i = 1; $i -le 15; $i++) { Move-Pointer ($FromX + $i) ($FromY + $i); Start-Sleep -Milliseconds 40 }
    for ($i = 1; $i -le 25; $i++) {
        Move-Pointer ([int]($FromX + ($ToX - $FromX) * $i / 25)) ([int]($FromY + ($ToY - $FromY) * $i / 25)); Start-Sleep -Milliseconds 50
    }
    Start-Sleep -Milliseconds 600
    Move-Pointer ($ToX + 2) ($ToY + 2); Start-Sleep -Milliseconds 400
    [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 2500
}
$script:shotName = $null
function Screen-Shot([string] $Name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size); $g.Dispose()
    $bmp.Save((Join-Path $run.Root "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
function Window-Titled([string] $Pattern, [int] $Seconds = 20) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $w = $AE::RootElement.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match $Pattern } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    }
    $null
}
function Place($Window) {
    <# Left of the screen, clear of the docked drawer on the right. #>
    [void][NotoVal.Drag]::SetWindowPos([IntPtr]$Window.Current.NativeWindowHandle, [IntPtr]::Zero, 40, 40, 1000, 760, 0x0040)
    Start-Sleep -Milliseconds 800
}
function Close-Window($Window) { if ($Window) { [void][NotoVal.Drag]::PostMessage([IntPtr]$Window.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 } }
function Item-In($Root, [string] $Name, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, $Name),
        [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = $Root.FindFirst($Scope::Descendants, $cond); if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 300 }
    $null
}
function Stack-Point { $l = Find-ById 'NoteList'; $r = $l.Current.BoundingRectangle; @([int]($r.X + $r.Width / 2), [int]($r.Y + [Math]::Min(120, $r.Height / 3))) }

$script:desktopFile = $null
$script:edgeProfile = $null
$script:log = [NotoVal.DebugLog]::new()
function Drops([int] $Mark) { (@($script:log.Lines($script:noto.Pid)) | Select-Object -Skip $Mark | Where-Object { $_ -match 'Noto\.Drop enter' } | ForEach-Object { ($_ -split 'formats=')[1] }) -join ' / ' }
function Mark { @($script:log.Lines($script:noto.Pid)).Count }
function Stop-Edge {
    <# Only the Edge started here: the processes whose command line names this run's profile. #>
    if (-not $script:edgeProfile) { return }
    $mine = @(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($script:edgeProfile) })
    foreach ($p in $mine) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
}

exit (Invoke-IsolatedRun $run -Title '#31: drag and drop from Explorer, the desktop and Edge' -Body {
  try {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Work', 1, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }
    $null = Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES ('01J00000000000000000000S01', `$f, 'Already here', 1, `$t, `$t)" @{ '$f' = $Work; '$t' = $now }
    Set-NotoSetting $run -Exe $NotoExe -DataRoot $script:data -Key 'workspace.hide-on-deactivation' -Value 'false'

    $files = Join-Path $run.Root "drag files $($run.Id)"; New-Item -ItemType Directory $files | Out-Null
    [IO.File]::WriteAllText((Join-Path $files 'one.md'), 'From Explorer')
    [IO.File]::WriteAllText((Join-Path $files 'two.md'), 'Second of two')
    [IO.File]::WriteAllText((Join-Path $files 'three.md'), 'Third of two')

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward
    $row = @((Find-ById 'FolderList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))[0]
    $row.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x28 1500

    # 1, 2 - Explorer
    Start-Process explorer.exe -ArgumentList "`"$files`""
    $explorer = Window-Titled ([regex]::Escape("drag files $($run.Id)"))
    if (-not $explorer) { throw 'the Explorer window did not open' }
    Place $explorer
    $item = Item-In $explorer 'one.md'
    if (-not $item) { throw "Explorer shows no 'one.md'" }
    $from = Centre $item; $to = Stack-Point
    $before = (Contents).Count; $m = Mark
    Real-Drag $from[0] $from[1] $to[0] $to[1] ([IntPtr]$explorer.Current.NativeWindowHandle) $null
    $notes = Contents
    Add-Result $run '1  Explorer, one file: dragged onto the stack, a note' ($notes.Count -eq $before + 1 -and $notes[0] -eq 'From Explorer' -and (Shown-Text 'NoteStatusText') -eq 'Imported 1 note.') "notes=$before->$($notes.Count) first='$($notes[0])' status='$(Shown-Text 'NoteStatusText')' from=($($from -join ',')) to=($($to -join ',')) offered=[$(Drops $m)]"

    $two = Item-In $explorer 'two.md'; $three = Item-In $explorer 'three.md'
    $two.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $three.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection()
    Start-Sleep -Milliseconds 500
    $from = Centre $three; $to = Stack-Point
    $before = (Contents).Count
    Real-Drag $from[0] $from[1] $to[0] $to[1] ([IntPtr]$explorer.Current.NativeWindowHandle) $null
    $notes = Contents
    Add-Result $run '2  Explorer, two files selected: one drag, two notes, one result' ($notes.Count -eq $before + 2 -and ($notes -contains 'Second of two') -and ($notes -contains 'Third of two') -and (Shown-Text 'NoteStatusText') -eq 'Imported 2 notes.') "notes=$before->$($notes.Count) status='$(Shown-Text 'NoteStatusText')'"
    Close-Window $explorer

    # 3 - the desktop
    if ((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -ErrorAction SilentlyContinue).HideIcons -eq 1) {
        Write-Host '  [NOT RUN] 3  the desktop: its icons are hidden on this machine (the user''s setting, not changed)'
    }
    else {
    $desktopDir = [Environment]::GetFolderPath('Desktop')
    $script:desktopFile = Join-Path $desktopDir "Noto drop $($run.Id).txt"
    [IO.File]::WriteAllText($script:desktopFile, 'From the desktop')
    Start-Sleep -Seconds 3
    $desk = $AE::RootElement.FindFirst($Scope::Children, [System.Windows.Automation.PropertyCondition]::new($AE::ClassNameProperty, 'Progman'))
    $icon = if ($desk) { Item-In $desk (Split-Path $script:desktopFile -LeafBase) 5 } else { $null }
    if (-not $icon -and $desk) { $icon = Item-In $desk (Split-Path $script:desktopFile -Leaf) 5 }
    if ($icon) {
        $from = Centre $icon; $to = Stack-Point
        $before = (Contents).Count
        Real-Drag $from[0] $from[1] $to[0] $to[1] ([IntPtr]::Zero) 'Progman|WorkerW'
        $notes = Contents
        Add-Result $run '3  the desktop: a file dragged from its icon onto the stack, a note; the file stays' ($notes.Count -eq $before + 1 -and $notes[0] -eq 'From the desktop' -and (Test-Path $script:desktopFile)) "notes=$before->$($notes.Count) first='$($notes[0])' file kept=$(Test-Path $script:desktopFile) from=($($from -join ','))"
    }
    else {
        Add-Invalid $run "3  the desktop icon for $(Split-Path $script:desktopFile -Leaf) was not found through UI Automation"
    }
    Remove-Item -LiteralPath $script:desktopFile -ErrorAction SilentlyContinue; $script:desktopFile = $null
    }

    # 4, 5 - Edge, with a profile of this run's
    $page = Join-Path $run.Root 'page.html'
    $bmp = New-Object System.Drawing.Bitmap 240, 140; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::Red); $g.Dispose(); $bmp.Save((Join-Path $run.Root 'red.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    [IO.File]::WriteAllText($page, "<!doctype html><html><head><title>Noto drop page $($run.Id)</title></head><body style='font:20px Segoe UI;margin:40px'><p><img src='red.png' alt='Red picture' width='240' height='140'></p><p id='t'>Draggable words here</p></body></html>")
    $script:edgeProfile = Join-Path $run.Root 'edge-profile'
    Start-Process $EdgeExe -ArgumentList "--user-data-dir=`"$script:edgeProfile`"", '--guest', '--disable-sync', '--no-first-run', '--no-default-browser-check', '--new-window', ([Uri]$page).AbsoluteUri
    $edge = Window-Titled ([regex]::Escape("Noto drop page $($run.Id)")) 30
    if (-not $edge) { Add-Invalid $run '4  Edge did not open the page'; return }
    Place $edge
    Start-Sleep -Seconds 2
    # Edge builds its accessibility tree once a client asks: looked for until it is there.
    $image = $null; $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $image) {
        $image = $edge.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Red picture'))
        if (-not $image) { Start-Sleep -Milliseconds 500 }
    }
    if ($image) {
        $from = Centre $image; $to = Stack-Point
        $before = (Contents).Count; $pictures = Attachments; $m = Mark
        Real-Drag $from[0] $from[1] $to[0] $to[1] ([IntPtr]$edge.Current.NativeWindowHandle) $null
        $notes = Contents
        $made = $notes.Count -eq $before + 1 -and $notes[0] -match '!\[[^\]]*\]\(attachments/' -and (Attachments) -eq $pictures + 1
        Add-Result $run '4  Edge, a picture dragged onto the stack: a note holding it' $made "notes=$before->$($notes.Count) first='$($notes[0])' pictures=$pictures->$(Attachments) status='$(Shown-Text 'NoteStatusText')' notice='$(Shown-Text 'NoteNoticeText')' offered=[$(Drops $m)]"
    }
    else { Add-Invalid $run '4  the page''s picture was not found through UI Automation' }

    # 5 - text from Edge into the note being edited
    $card = @((Find-ById 'NoteList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)) | Where-Object { $_.Current.Name -eq 'Already here' })[0]
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Bring-Forward
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x0D 1200
    $editor = Find-ById 'NoteEditor'
    $words = $null; $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $words) {
        $words = $edge.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, 'Draggable words here'))
        if (-not $words) { Start-Sleep -Milliseconds 500 }
    }
    if ($words) {
        # A double click on the first word selects it, as a user would; the drag then starts on the selection.
        $r = $words.Current.BoundingRectangle
        $wx = [int]($r.X + 30); $wy = [int]($r.Y + $r.Height / 2)
        if ([NotoVal.Drag]::RootAt($wx, $wy) -ne [IntPtr]$edge.Current.NativeWindowHandle) { throw "refusing to click at ($wx,$wy): it is not Edge" }
        Move-Pointer $wx $wy; Start-Sleep -Milliseconds 200
        [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80
        [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 700
        $from = @($wx, $wy); $to = Centre $editor; $m = Mark
        Real-Drag $from[0] $from[1] $to[0] $to[1] ([IntPtr]$edge.Current.NativeWindowHandle) $null
        $value = $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Add-Result $run '5  Edge, a selected word dragged into the note being edited: the word is added' ($value -match 'Draggable' -and $value -match 'Already here') "editor='$($value -replace "`n", ' | ')' offered=[$(Drops $m)]"
    }
    else { Add-Invalid $run '5  the page''s text was not found through UI Automation' }
    Bring-Forward
    Send-ToNoto @() 0x1B 900
    Close-Window $edge
  }
  finally {
    if ($script:desktopFile) { Remove-Item -LiteralPath $script:desktopFile -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    Stop-Edge
    # The profile is this run's alone, and never kept: a guest window signs in to nothing, and the folder goes anyway.
    if ($script:edgeProfile -and (Test-Path -LiteralPath $script:edgeProfile)) { Start-Sleep -Seconds 2; Remove-Item -LiteralPath $script:edgeProfile -Recurse -Force -ErrorAction SilentlyContinue }
  }
})
