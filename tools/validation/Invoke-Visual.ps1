#Requires -Version 7.4
<#
.SYNOPSIS
  Visual acceptance, Light and Dark (the visual refresh): every surface captured for review, and what can be measured,
  measured: the top edge, the folder rows, the selected state, the cards' rhythm, the type hierarchy, the edited card's
  edge and the Settings controls' right edge.

.DESCRIPTION
  A fresh isolated data root seeded with two folders of realistic notes: a pinned folder and note, an image-rich note
  whose pictures this script draws, and a binned note and folder for the Recycle Bin. For each theme, Noto is started,
  brought forward with the activation hotkey, and every surface is captured with PrintWindow (what the compositor
  draws). Keys reach Noto only while it is verifiably the foreground window. High contrast is the user's own palette
  and a system setting this script never changes: its guards are DesignSystemGuardTests'.

  For each theme (Light is 1-6, Dark 7-12), then 13:
     1  the top 8 pixel rows are the workspace's own surface, not a light strip
     2  folder rows are one height; the selected row has the accent pill and a fill off the sidebar, the other neither
     3  note cards: one gap between each, every card the same width and left edge
     4  type: the surface title is the heading size, a step above the body
     5  the edited card takes the accent on its edge
     6  Settings: every control in every group ends at the same right edge
    13  every surface captured in both themes: folders, stack, editor, image-rich note, search, settings, recycle bin
    15  per theme: a coloured card (F7, #150) is tinted off an uncoloured card's fill, and its help text names the colour
    14  per theme: a folder's context menu and a Settings drop-down list are drawn in that theme (F2: the window's
        content is pinned Dark under a see-through window, and the chosen theme is set on the drawer below it)

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Visual.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe, [int] $Tolerance = 12)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
if (-not ('NotoVal.Capture' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace NotoVal {
    public static class Capture {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        // PW_RENDERFULLCONTENT (2): the composited content, which is what a WinUI window draws.
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    }
}
'@
}
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'visual'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$TP = [System.Windows.Automation.TextPattern]
$Work = '01J0000000000000000000F051'; $HomeFolder = '01J0000000000000000000F052'; $OldFolder = '01J0000000000000000000F053'
# ULIDs: Crockford base32, no I, L, O or U.
$Harbour = '01J00000000000000000000P01'; $Hills = '01J00000000000000000000P02'
$Notes = @(
    @('01J00000000000000000000V01', $Work, "## Weekly sync`n`n**Attendees:** Priya, Tom, me`n`n- [ ] Tom: fix the *startup* crash`n- [x] book the room`n`nSee [the RFC](https://example.com/rfc) and ``release.md``.", 1, $null),
    @('01J00000000000000000000V02', $Work, "Project plan`n`nMilestones, owners and the release date. https://example.com/plan", 0, $null),
    @('01J00000000000000000000V04', $Work, "Trip photos`n![The harbour at dusk](attachments/$Harbour.png)`nThe harbour at dusk, from the hill path.`n![The hills](attachments/$Hills.png)", 0, $null),
    @('01J00000000000000000000V03', $HomeFolder, "# Groceries`n- [ ] milk`n- [x] bread`n- [ ] **coffee**", 0, $null),
    @('01J00000000000000000000V05', $HomeFolder, "Draft agenda`n`nAn older draft, moved to the bin.", 0, 'binned')
)

function Sql($Data, [string] $Text, [hashtable] $Parameters = @{}) {
    $db = Join-Path $Data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $Text; foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }; [void]$cmd.ExecuteNonQuery() }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Items($List) { @($List.FindAll($Scope::Children, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)) | Where-Object { -not $_.Current.IsOffscreen }) }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 600) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Type-Text([string] $Text) {
    foreach ($ch in $Text.ToCharArray()) {
        if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to type: Noto is not the foreground window' }
        $down = New-Object NotoVal.Win32+INPUT; $down.type = 1; $down.wScan = [uint16]$ch; $down.dwFlags = 4
        $up = New-Object NotoVal.Win32+INPUT; $up.type = 1; $up.wScan = [uint16]$ch; $up.dwFlags = 6
        [NotoVal.Win32]::Keys(@($down, $up)); Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 500
}
function Bring-Forward {
    $null = Set-ForegroundByKeyboard $script:target
    Send-ActivationChord -AllowedForeground $script:target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}
function Capture-Window {
    $r = New-Object NotoVal.Capture+RECT; [void][NotoVal.Capture]::GetWindowRect($script:noto.Hwnd, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { [void][NotoVal.Capture]::PrintWindow($script:noto.Hwnd, $dc, 2) } finally { $g.ReleaseHdc($dc); $g.Dispose() }
    [pscustomobject]@{ Bitmap = $bmp; Left = $r.L; Top = $r.T }
}
function Shot([string] $Name) {
    $c = Capture-Window
    try { $c.Bitmap.Save((Join-Path $run.Root "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $c.Bitmap.Dispose() }
    "$Name.png"
}
function Top-Edge {
    <#
      The largest channel difference between any pixel of the 8 rows below the top and the surface 40 rows down, across
      the middle of the window. Row 0 is the drawer's own 1 px top border (F2, #145), so it is left out.
    #>
    $c = Capture-Window; $bmp = $c.Bitmap
    try {
        $worst = 0
        for ($x = [int]($bmp.Width * 0.2); $x -lt [int]($bmp.Width * 0.8); $x += 4) {
            $surface = $bmp.GetPixel($x, 40)
            for ($y = 1; $y -le 8; $y++) {
                $p = $bmp.GetPixel($x, $y)
                $worst = [Math]::Max($worst, [Math]::Max([Math]::Abs($p.R - $surface.R), [Math]::Max([Math]::Abs($p.G - $surface.G), [Math]::Abs($p.B - $surface.B))))
            }
        }
        $s = $bmp.GetPixel([int]($bmp.Width / 2), 40); $t = $bmp.GetPixel([int]($bmp.Width / 2), 2)
        [pscustomobject]@{ Worst = $worst; Surface = '#{0:X2}{1:X2}{2:X2}' -f $s.R, $s.G, $s.B; Top = '#{0:X2}{1:X2}{2:X2}' -f $t.R, $t.G, $t.B }
    }
    finally { $bmp.Dispose() }
}
function Accent([System.Drawing.Color] $p) { ([Math]::Max($p.R, [Math]::Max($p.G, $p.B)) - [Math]::Min($p.R, [Math]::Min($p.G, $p.B))) -ge 60 }
function Region-Stats($Rect, [int] $Band = 0) {
    <#
      Inside a screen rectangle (or only its left $Band pixels): how many pixels are a saturated colour (the accent;
      Noto's own surfaces and text are grey), and the most common grey fill.
    #>
    $c = Capture-Window; $bmp = $c.Bitmap
    try {
        $x0 = [Math]::Max(0, [int]$Rect.X - $c.Left); $y0 = [Math]::Max(0, [int]$Rect.Y - $c.Top)
        $x1 = [Math]::Min($bmp.Width, [int]($Rect.X + $Rect.Width) - $c.Left); $y1 = [Math]::Min($bmp.Height, [int]($Rect.Y + $Rect.Height) - $c.Top)
        if ($Band -gt 0) { $x1 = [Math]::Min($x1, $x0 + $Band) }
        $accent = 0; $fills = @{}
        for ($y = $y0; $y -lt $y1; $y++) {
            for ($x = $x0; $x -lt $x1; $x++) {
                $p = $bmp.GetPixel($x, $y)
                if (Accent $p) { $accent++ } else { $fills[$p.ToArgb()] = 1 + [int]$fills[$p.ToArgb()] }
            }
        }
        $fill = [System.Drawing.Color]::FromArgb(($fills.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key)
        [pscustomobject]@{ Accent = $accent; Fill = $fill }
    }
    finally { $bmp.Dispose() }
}
function Edge-Accent($Rect) {
    <# The share of the pixels along a rectangle's top edge (the two rows at it) that are the accent. #>
    $c = Capture-Window; $bmp = $c.Bitmap
    try {
        $x0 = [int]$Rect.X - $c.Left + 12; $x1 = [int]($Rect.X + $Rect.Width) - $c.Left - 12; $y = [int]$Rect.Y - $c.Top
        $hit = 0; $all = 0
        for ($x = $x0; $x -lt $x1; $x += 2) { $all++; if ((Accent $bmp.GetPixel($x, $y)) -or (Accent $bmp.GetPixel($x, $y + 1))) { $hit++ } }
        if ($all -eq 0) { 0 } else { $hit / $all }
    }
    finally { $bmp.Dispose() }
}
function Switch-Track($Element) {
    <#
      An "on" switch's track as drawn (its accent pixels inside the element): whether its rounded ends are both whole
      (at the track's top row, the two ends are inset alike: a clipped end is square) and how far its right end stands
      from the element's right edge.
    #>
    $c = Capture-Window; $bmp = $c.Bitmap
    try {
        $r = $Element.Current.BoundingRectangle
        $x0 = [int]$r.X - $c.Left; $x1 = [int]($r.X + $r.Width) - $c.Left; $y0 = [int]$r.Y - $c.Top; $y1 = [int]($r.Y + $r.Height) - $c.Top
        $rows = @(for ($y = $y0; $y -lt $y1; $y++) {
                $xs = @(for ($x = $x0; $x -lt $x1; $x++) { if (Accent $bmp.GetPixel($x, $y)) { $x } })
                if ($xs.Count -ge 6) { [pscustomobject]@{ Y = $y; Min = $xs[0]; Max = $xs[-1] } }
            })
        if ($rows.Count -eq 0) { return $null }
        $left = ($rows | Measure-Object Min -Minimum).Minimum; $right = ($rows | Measure-Object Max -Maximum).Maximum
        [pscustomobject]@{ Width = $right - $left + 1; TopLeftInset = $rows[0].Min - $left; TopRightInset = $right - $rows[0].Max; FromEdge = $x1 - 1 - $right }
    }
    finally { $bmp.Dispose() }
}
function Screen-Pixel([int] $X, [int] $Y) {
    $b = New-Object System.Drawing.Bitmap 1, 1
    try { $g = [System.Drawing.Graphics]::FromImage($b); try { $g.CopyFromScreen($X, $Y, 0, 0, $b.Size) } finally { $g.Dispose() }; $b.GetPixel(0, 0) } finally { $b.Dispose() }
}
function Popup-Fill([string] $ClassName, [switch] $Right) {
    <# The fill of the last open popup item of that class, read off the screen: a popup is not in the window's capture. #>
    $items = @($AE::RootElement.FindAll($Scope::Descendants, [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new($AE::ClassNameProperty, $ClassName), [System.Windows.Automation.PropertyCondition]::new($AE::ProcessIdProperty, $script:noto.Pid))) | Where-Object { -not $_.Current.IsOffscreen })
    if (-not $items.Count) { return $null }
    $r = $items[-1].Current.BoundingRectangle
    Screen-Pixel ([int]$(if ($Right) { $r.Right - 6 } else { $r.Left + 4 })) ([int]($r.Top + $r.Height / 2))
}
function In-Theme([string] $Theme, $Pixel) { if (-not $Pixel) { return $false }; $l = ($Pixel.R + $Pixel.G + $Pixel.B) / 3; if ($Theme -eq 'Light') { $l -gt 180 } else { $l -lt 90 } }
function Distance([System.Drawing.Color] $a, [System.Drawing.Color] $b) { [Math]::Max([Math]::Abs($a.R - $b.R), [Math]::Max([Math]::Abs($a.G - $b.G), [Math]::Abs($a.B - $b.B))) }
function Font-Size($Element) {
    try { [double]$Element.GetCurrentPattern($TP::Pattern).DocumentRange.GetAttributeValue($TP::FontSizeAttribute) } catch { $null }
}
function New-Photo([string] $Path, [System.Drawing.Color] $From, [System.Drawing.Color] $To) {
    Assert-UnderRun $run $Path
    $bmp = New-Object System.Drawing.Bitmap 800, 450
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Rectangle]::new(0, 0, 800, 450)), $From, $To, 60
            $g.FillRectangle($brush, 0, 0, 800, 450); $brush.Dispose()
        }
        finally { $g.Dispose() }
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bmp.Dispose() }
}

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'Visual acceptance' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $pictures = Join-Path $script:data 'attachments'; New-Item -ItemType Directory $pictures -Force | Out-Null
    New-Photo (Join-Path $pictures "$Harbour.png") ([System.Drawing.Color]::FromArgb(255, 32, 72, 120)) ([System.Drawing.Color]::FromArgb(255, 236, 160, 96))
    New-Photo (Join-Path $pictures "$Hills.png") ([System.Drawing.Color]::FromArgb(255, 70, 120, 60)) ([System.Drawing.Color]::FromArgb(255, 200, 220, 170))

    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $order = 0
    foreach ($folder in @(@($Work, 'Work', 1, $null), @($HomeFolder, 'Home', 0, $null), @($OldFolder, 'Old projects', 0, $now))) {
        $order++
        Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, IsPinned, CreatedAt, UpdatedAt, DeletedAt) VALUES (`$f, `$n, `$s, `$p, `$t, `$t, `$d)" @{ '$f' = $folder[0]; '$n' = $folder[1]; '$s' = $order; '$p' = $folder[2]; '$t' = $now; '$d' = [object]$folder[3] ?? [DBNull]::Value }
    }
    foreach ($note in $Notes) {
        $order++
        $deleted = if ($note[4]) { $now } else { [DBNull]::Value }
        Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsPinned, CreatedAt, UpdatedAt, DeletedAt) VALUES (`$n, `$f, `$c, `$s, `$p, `$t, `$t, `$d)" @{ '$n' = $note[0]; '$f' = $note[1]; '$c' = $note[2]; '$s' = $order; '$p' = $note[3]; '$t' = $now; '$d' = $deleted }
    }
    # F7 (#150): one card coloured Blue (note3), for case 15.
    Sql $script:data "UPDATE Notes SET ColorKey = 'note3' WHERE Id = '01J00000000000000000000V02'"

    $shots = @()
    $base = 0
    foreach ($theme in 'Light', 'Dark') {
        $t = $theme.ToLower()
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $script:data -Key 'appearance.theme' -Value $theme
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
        Bring-Forward
        $scale = [NotoVal.Capture]::GetDpiForWindow($script:noto.Hwnd) / 96.0

        # 1 - the top edge
        $folders = Items (Find-ById 'FolderList')
        $folders[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400
        $edge = Top-Edge
        Add-Result $run "$($base + 1)  ${theme}: the top 8 pixel rows are the workspace's own surface" ($edge.Worst -le $Tolerance) "worst channel difference=$($edge.Worst) (tolerance $Tolerance) top=$($edge.Top) surface=$($edge.Surface)"

        # 2 - folder rows and the selected state (selected by the list, not focused, so no focus ring)
        $heights = @($folders | ForEach-Object { [int]$_.Current.BoundingRectangle.Height })
        $selected = Region-Stats $folders[0].Current.BoundingRectangle ([int](8 * $scale))
        $other = Region-Stats $folders[1].Current.BoundingRectangle ([int](8 * $scale))
        $fillSelected = (Region-Stats $folders[0].Current.BoundingRectangle).Fill; $fillOther = (Region-Stats $folders[1].Current.BoundingRectangle).Fill
        $sameHeight = ($heights | Sort-Object -Unique).Count -eq 1
        Add-Result $run "$($base + 2)  ${theme}: folder rows one height; the selected row has the pill and a fill, the other neither" ($sameHeight -and $selected.Accent -ge 10 -and $other.Accent -eq 0 -and (Distance $fillSelected $fillOther) -ge 8) "heights=$($heights -join ',') pill px selected=$($selected.Accent) other=$($other.Accent) fills=$($fillSelected.Name)/$($fillOther.Name)"
        $shots += Shot "$t-folders"

        # 14 - a folder's context menu, opened by Shift+F10 on the first folder row
        $folders[0].SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @(0x10) 0x79 700
        $menuFill = Popup-Fill 'MenuFlyoutItem'
        Send-ToNoto @() 0x1B 500
        Add-Result $run "14 ${theme}: a folder's context menu is drawn in $theme" (In-Theme $theme $menuFill) "fill=$(if ($menuFill) { $menuFill.Name } else { 'no menu' })"

        # the Recycle Bin, from the folder list
        (Find-ById 'RecycleBinButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200
        $null = Find-ById 'RecycleBinRoot'
        $shots += Shot "$t-recycle-bin"
        Send-ToNoto @() 0x1B 1000

        # 3 - note cards
        $folders = Items (Find-ById 'FolderList')
        $folders[0].SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @(0x11) 0x28 1200
        $cards = Items (Find-ById 'NoteList')
        $rects = @($cards | ForEach-Object { $_.Current.BoundingRectangle })
        $gaps = @(for ($i = 1; $i -lt $rects.Count; $i++) { [int]($rects[$i].Y - ($rects[$i - 1].Y + $rects[$i - 1].Height)) })
        $expected = [Math]::Round(8 * $scale)
        $lefts = @($rects | ForEach-Object { [int]$_.X } | Sort-Object -Unique); $widths = @($rects | ForEach-Object { [int]$_.Width } | Sort-Object -Unique)
        Add-Result $run "$($base + 3)  ${theme}: one gap between cards; every card the same width and left edge" ($gaps.Count -ge 1 -and @($gaps | Where-Object { [Math]::Abs($_ - $expected) -gt 1 }).Count -eq 0 -and $lefts.Count -eq 1 -and $widths.Count -eq 1) "cards=$($rects.Count) gaps=$($gaps -join ',') (expected $expected) lefts=$($lefts -join ',') widths=$($widths -join ',')"
        $shots += Shot "$t-stack"

        # 15 - F7 (#150): a coloured card is tinted, off an uncoloured card's fill, and names its colour
        $cards = Items (Find-ById 'NoteList')
        $blue = $cards | Where-Object { $_.Current.Name -eq 'Project plan' } | Select-Object -First 1
        $plain = $cards | Where-Object { $_.Current.Name -ne 'Project plan' } | Select-Object -First 1
        $fb = if ($blue) { (Region-Stats $blue.Current.BoundingRectangle).Fill }; $fp = if ($plain) { (Region-Stats $plain.Current.BoundingRectangle).Fill }
        Add-Result $run "15 ${theme}: a coloured card (Blue) is tinted off an uncoloured card, and its help text names the colour" ($fb -and $fp -and (Distance $fb $fp) -ge 6 -and $blue.Current.HelpText -match 'Blue') "blue fill=$(if ($fb) { $fb.Name }) plain fill=$(if ($fp) { $fp.Name }) help='$(if ($blue) { $blue.Current.HelpText })'"

        # 4 - type hierarchy
        $title = Font-Size (Find-ById 'NoteFolderTitle'); $body = Font-Size (Find-ById 'NoteCountText')
        Add-Result $run "$($base + 4)  ${theme}: the surface title is the heading size, a step above the body" ($title -eq 20 -and $body -eq 14) "title=$title body=$body"

        # 5 - the edited card's edge
        $cards[0].SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @() 0x0D 1200
        $edited = @(Items (Find-ById 'NoteList'))[0].Current.BoundingRectangle
        $share = Edge-Accent $edited
        Add-Result $run "$($base + 5)  ${theme}: the edited card takes the accent on its edge" ($share -ge 0.8) ("accent share of the top edge={0:P0}" -f $share)
        $shots += Shot "$t-editor"
        Send-ToNoto @() 0x1B 900

        # the image-rich note, scrolled into view
        $photos = @((Find-ById 'NoteList').FindAll($Scope::Children, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)) | Where-Object { $_.Current.Name -eq 'Trip photos' })[0]
        try { $photos.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
        Start-Sleep -Milliseconds 1200
        $shots += Shot "$t-images"

        # 6 - Settings: one right edge
        Send-ToNoto @(0x11) 0xBC 1400
        $settings = Find-ById 'SettingsRoot'
        $controls = @($settings.FindAll($Scope::Descendants, [System.Windows.Automation.OrCondition]::new(
                    [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Button),
                    [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ComboBox))) |
                Where-Object { -not $_.Current.IsOffscreen -and $_.Current.AutomationId -ne 'SettingsBackButton' -and $_.Current.BoundingRectangle.Width -gt 0 })
        $rows = @($controls | Where-Object { $_.Current.Name -ne 'Change' })   # the shortcut row's Change sits inside its own box
        $rights = @($rows | ForEach-Object { [int]($_.Current.BoundingRectangle.X + $_.Current.BoundingRectangle.Width) })
        $side = Find-ById 'SideSetting'
        $side.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 600
        $listFill = Popup-Fill 'ComboBoxItem' -Right
        try { $side.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
        Start-Sleep -Milliseconds 400
        $spread = if ($rights.Count) { ($rights | Measure-Object -Maximum).Maximum - ($rights | Measure-Object -Minimum).Minimum } else { -1 }
        # ... and, as drawn, an "on" switch is whole (both rounded ends) and its track ends at that edge: the element's
        # box alone measured flush while the template clipped the track's end (the visual refresh's review).
        $on = @($rows | Where-Object { try { $_.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' } catch { $false } })
        $tracks = @($on | ForEach-Object { Switch-Track $_ })
        $whole = $tracks.Count -ge 1 -and @($tracks | Where-Object { $null -eq $_ -or [Math]::Abs($_.TopLeftInset - $_.TopRightInset) -gt 1 -or $_.FromEdge -gt 2 }).Count -eq 0
        $drawn = ($tracks | ForEach-Object { if ($_) { "w$($_.Width) ends $($_.TopLeftInset)/$($_.TopRightInset) edge+$($_.FromEdge)" } else { 'none' } }) -join '; '
        Add-Result $run "$($base + 6)  ${theme}: every Settings control ends at the same right edge; a switch is drawn whole, flush with it" ($rights.Count -ge 6 -and $spread -le 1 -and $whole) "controls=$($rights.Count) right edges=$(($rights | Sort-Object -Unique) -join ',') spread=$spread; on switches: $drawn"
        $shots += Shot "$t-settings"
        Add-Result $run "14 ${theme}: a Settings drop-down list is drawn in $theme" (In-Theme $theme $listFill) "fill=$(if ($listFill) { $listFill.Name } else { 'no list' })"
        Send-ToNoto @() 0x1B 900

        Send-ToNoto @(0x11) 0x46 900
        Type-Text 'sync'
        Start-Sleep -Milliseconds 600
        $shots += Shot "$t-search"
        Send-ToNoto @() 0x1B 900
        if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
        $base += 6
    }

    Add-Result $run '13 every surface captured in both themes (for review)' ($shots.Count -eq 14) ($shots -join ' ')
}) }
finally {
    $script:log.Dispose()
}
