#Requires -Version 7.4
<#
.SYNOPSIS
  Visual snapshots for review, Light and Dark: the folder list, a note stack, the editor, Settings and Search, and one
  measured check: the window's top edge is the workspace's own surface, not a light strip.

.DESCRIPTION
  A fresh isolated data root seeded with two folders of realistic notes. For each theme, Noto is started, brought
  forward with the activation hotkey, and every surface is captured with PrintWindow (what the compositor draws). Keys
  reach Noto only while it is verifiably the foreground window.

     1  Light: the top 8 pixel rows match the surface below them (within a small tolerance)
     2  Dark: the same
     3  every surface captured in both themes (screenshots in the run for review)

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
$Work = '01J0000000000000000000F051'; $HomeFolder = '01J0000000000000000000F052'
$Notes = @(
    @('01J00000000000000000000V01', $Work, "## Weekly sync`n`n**Attendees:** Priya, Tom, me`n`n- [ ] Tom: fix the *startup* crash`n- [x] book the room`n`nSee [the RFC](https://example.com/rfc) and ``release.md``."),
    @('01J00000000000000000000V02', $Work, "Project plan`n`nMilestones, owners and the release date. https://example.com/plan"),
    @('01J00000000000000000000V03', $HomeFolder, "# Groceries`n- [ ] milk`n- [x] bread`n- [ ] **coffee**")
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
    $bmp
}
function Shot([string] $Name) {
    $bmp = Capture-Window
    try { $bmp.Save((Join-Path $run.Root "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
    "$Name.png"
}
function Top-Edge {
    <# The largest channel difference between any pixel of the top 8 rows and the surface 40 rows down, across the middle of the window. #>
    $bmp = Capture-Window
    try {
        $worst = 0
        for ($x = [int]($bmp.Width * 0.2); $x -lt [int]($bmp.Width * 0.8); $x += 4) {
            $surface = $bmp.GetPixel($x, 40)
            for ($y = 0; $y -lt 8; $y++) {
                $p = $bmp.GetPixel($x, $y)
                $worst = [Math]::Max($worst, [Math]::Max([Math]::Abs($p.R - $surface.R), [Math]::Max([Math]::Abs($p.G - $surface.G), [Math]::Abs($p.B - $surface.B))))
            }
        }
        $s = $bmp.GetPixel([int]($bmp.Width / 2), 40); $t = $bmp.GetPixel([int]($bmp.Width / 2), 2)
        [pscustomobject]@{ Worst = $worst; Surface = '#{0:X2}{1:X2}{2:X2}' -f $s.R, $s.G, $s.B; Top = '#{0:X2}{1:X2}{2:X2}' -f $t.R, $t.G, $t.B }
    }
    finally { $bmp.Dispose() }
}

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'Visual snapshots' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $order = 0
    foreach ($folder in @(@($Work, 'Work'), @($HomeFolder, 'Home'))) { $order++; Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, `$n, `$s, `$t, `$t)" @{ '$f' = $folder[0]; '$n' = $folder[1]; '$s' = $order; '$t' = $now } }
    foreach ($note in $Notes) { $order++; Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" @{ '$n' = $note[0]; '$f' = $note[1]; '$c' = $note[2]; '$s' = $order; '$t' = $now } }

    $shots = @()
    $case = 0
    foreach ($theme in 'Light', 'Dark') {
        $case++
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $script:data -Key 'appearance.theme' -Value $theme
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
        Bring-Forward
        $folder = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
        $folder.SetFocus(); Start-Sleep -Milliseconds 500
        $edge = Top-Edge
        $shots += Shot "$($theme.ToLower())-folders"
        Add-Result $run "$case  ${theme}: the top 8 pixel rows are the workspace's own surface" ($edge.Worst -le $Tolerance) "worst channel difference=$($edge.Worst) (tolerance $Tolerance) top=$($edge.Top) surface=$($edge.Surface)"

        Send-ToNoto @(0x11) 0x28 1000
        $shots += Shot "$($theme.ToLower())-stack"
        $card = (Find-ById 'NoteList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
        $card.SetFocus(); Start-Sleep -Milliseconds 300
        Send-ToNoto @() 0x0D 1000
        $shots += Shot "$($theme.ToLower())-editor"
        Send-ToNoto @() 0x1B 800
        Send-ToNoto @(0x11) 0xBC 1200
        $null = Find-ById 'SettingsRoot'
        $shots += Shot "$($theme.ToLower())-settings"
        Send-ToNoto @() 0x1B 900
        Send-ToNoto @(0x11) 0x46 900
        Type-Text 'meet'
        $shots += Shot "$($theme.ToLower())-search"
        Send-ToNoto @() 0x1B 900
        if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
    }

    Add-Result $run '3  every surface captured in both themes (for review)' ($shots.Count -eq 10) ($shots -join ' ')
}) }
finally {
    $script:log.Dispose()
}
