#Requires -Version 7.4
<#
.SYNOPSIS
  The welcome (ADR-021) on a running build: shown only for a new workspace, walked by keyboard alone, finished,
  skipped, put away with Escape, opened again from Settings > Help, and never shown again once it has ended; kept
  owed across a start that crashed; never shown for an existing workspace; captured in Light and Dark.

.DESCRIPTION
  Each case starts Noto on a data root of its own under the run's directory, so "new workspace" is literal. Keys reach
  Noto only while it is verifiably the foreground window, brought there by the activation hotkey; buttons are pressed
  by keys, except Skip in case 7, pressed through UI Automation as a pointer would. Where the welcome stands is read
  from the database itself (onboarding.welcome). High contrast is a system setting this script never changes: its
  guards are DesignSystemGuardTests' and WelcomeGuardTests'.

     1  a new workspace opens with the welcome: Step 1 of 4, its title and lines, focus on Next, no Back; owed (Due)
     2  keyboard only: Enter walks steps 2, 3 and 4, each with its title and progress; the last action is Get started
     3  Shift+Tab then Enter on Back goes back; on step 1 Back is gone and focus is on Next again
     4  Tab leaves the welcome and comes back: no keyboard trap; Skip, Back and Next in reading order
     5  Get started closes it to the folder list, focus on the name box; recorded Completed
     6  a completed welcome is not shown at the next start
     7  Settings > Help > Show tour opens it at step 1; Escape goes back to Settings, focus on Show tour
     8  Skip (a second new workspace) closes it; recorded Skipped; two more starts never show it
     9  Escape (a third new workspace) closes it; recorded Skipped
    10  a start killed with the welcome open (a fourth new workspace): the next start shows it again, still owed
    11  an existing workspace from a Noto without the welcome (no row) never shows it: with a folder, and empty
    12  Light and Dark: every step captured for review; the column and its actions fit inside the window
    13  every start in the run ended cleanly (no crash)

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Welcome.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
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

$run = New-ValidationRun 'welcome'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Invoke = [System.Windows.Automation.InvokePattern]::Pattern
$VK_TAB = 0x09; $VK_ENTER = 0x0D; $VK_ESC = 0x1B; $VK_SHIFT = 0x10; $VK_CTRL = 0x11; $VK_COMMA = 0xBC
$Titles = 'Welcome to Noto', 'Organize', 'Write', 'Your data'

function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Shown([string] $Id) { $e = By-Id $Id; [bool]($e -and -not $e.Current.IsOffscreen) }
function Gone([string] $Id, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (-not (Shown $Id)) { return $true }; Start-Sleep -Milliseconds 200 }
    $false
}
function Text-Of([string] $Id) { $e = By-Id $Id; if ($e) { $e.Current.Name } else { '' } }
function Focused { $f = $AE::FocusedElement; if ($f) { $f.Current.AutomationId } else { '' } }
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
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:noto.Hwnd) { Start-Sleep -Milliseconds 600; return }
        Start-Sleep -Milliseconds 1000
    }
    throw 'the hotkey did not bring Noto forward'
}
function Step { [pscustomobject]@{ Progress = Text-Of 'WelcomeProgressText'; Title = Text-Of 'WelcomeTitle'; Next = Text-Of 'WelcomeNextButton'; Back = Shown 'WelcomeBackButton'; Lines = Lines-Read } }
function Lines-Read {
    <# The step's lines as a screen reader finds them: the page's text that is not its progress, title, summary or a button's label. #>
    $known = @((Text-Of 'WelcomeProgressText'), (Text-Of 'WelcomeTitle'), (Text-Of 'WelcomeSummary'), 'Skip', 'Back', 'Next', 'Get started')
    @((By-Id 'WelcomeRoot').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -notin $known }).Count
}
function State([string] $Data) { [string](Get-NotoScalar $run -Exe $NotoExe -DataRoot $Data -Sql "SELECT Value FROM Settings WHERE Key = 'onboarding.welcome'") }
function New-Root([string] $Name) { $d = Join-Path $run.Root $Name; New-Item -ItemType Directory $d | Out-Null; $d }
function Start-On([string] $Data, [switch] $Keep) { $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $Data -KeepWelcome:$Keep; $script:noto }
function Stop-Clean { if (Stop-Noto $script:noto) { $script:clean++ } else { $script:unclean++ } }
function Shot([string] $Name) {
    $r = New-Object NotoVal.Capture+RECT; [void][NotoVal.Capture]::GetWindowRect($script:noto.Hwnd, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { [void][NotoVal.Capture]::PrintWindow($script:noto.Hwnd, $dc, 2) } finally { $g.ReleaseHdc($dc); $g.Dispose() }
    try { $bmp.Save((Join-Path $run.Root "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
    "$Name.png"
}
function Fits {
    <# The parts of the welcome on screen that are not wholly inside the window: none, or their ids. #>
    $r = New-Object NotoVal.Capture+RECT; [void][NotoVal.Capture]::GetWindowRect($script:noto.Hwnd, [ref]$r)
    $out = foreach ($id in 'WelcomeProgressText', 'WelcomeTitle', 'WelcomeSummary', 'WelcomeLines', 'WelcomeSkipButton', 'WelcomeBackButton', 'WelcomeNextButton') {
        $e = By-Id $id
        if (-not $e -or $e.Current.IsOffscreen) { continue }
        $b = $e.Current.BoundingRectangle
        if ($b.Width -le 0 -or $b.X -lt $r.L -or ($b.X + $b.Width) -gt $r.R -or $b.Y -lt $r.T -or ($b.Y + $b.Height) -gt $r.B) { "$id[$([int]$b.X),$([int]$b.Y) $([int]$b.Width)x$([int]$b.Height) in $($r.L),$($r.T)-$($r.R),$($r.B)]" }
    }
    if ($out) { $out -join ' ' } else { 'ok' }
}

$script:log = [NotoVal.DebugLog]::new()
$script:clean = 0; $script:unclean = 0
try {
exit (Invoke-IsolatedRun $run -Title 'Welcome (ADR-021)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run

    # 1 - a new workspace
    $a = New-Root 'a'
    $null = Start-On $a -Keep
    Bring-Forward
    $s = Step; $focus = Focused
    Add-Result $run '1  a new workspace opens with the welcome: Step 1 of 4, its title and lines, focus on Next, no Back; owed' ((Shown 'WelcomeRoot') -and $s.Progress -eq 'Step 1 of 4' -and $s.Title -eq $Titles[0] -and $s.Next -eq 'Next' -and -not $s.Back -and $s.Lines -eq 3 -and $focus -eq 'WelcomeNextButton' -and (State $a) -eq 'Due' -and -not (Shown 'FolderList')) "progress='$($s.Progress)' title='$($s.Title)' lines=$($s.Lines) next='$($s.Next)' back=$($s.Back) focus='$focus' state='$(State $a)'"

    # 2 - Enter walks the steps
    $walk = @(for ($i = 1; $i -le 3; $i++) { Send-ToNoto @() $VK_ENTER; Step })
    $ok = $true
    for ($i = 0; $i -lt 3; $i++) { $ok = $ok -and $walk[$i].Progress -eq "Step $($i + 2) of 4" -and $walk[$i].Title -eq $Titles[$i + 1] -and $walk[$i].Back }
    Add-Result $run '2  keyboard only: Enter walks steps 2, 3 and 4; the last action is Get started' ($ok -and $walk[2].Next -eq 'Get started' -and $walk[1].Next -eq 'Next' -and (Focused) -eq 'WelcomeNextButton') (($walk | ForEach-Object { "$($_.Progress) '$($_.Title)' [$($_.Next)]" }) -join ' | ')

    # 3 - Back by keyboard, to the first step
    Send-ToNoto @($VK_SHIFT) $VK_TAB
    $onBack = Focused
    $back = @(for ($i = 1; $i -le 3; $i++) { Send-ToNoto @() $VK_ENTER; Step })
    $focus = Focused
    Add-Result $run '3  Shift+Tab, Enter on Back goes back; on step 1 Back is gone and focus is on Next' ($onBack -eq 'WelcomeBackButton' -and ((($back | ForEach-Object Title) -join '|') -eq (($Titles[2], $Titles[1], $Titles[0]) -join '|')) -and -not $back[2].Back -and $focus -eq 'WelcomeNextButton') "on '$onBack'; $(($back | ForEach-Object { $_.Progress }) -join ' > ') back=$($back[2].Back) focus='$focus'"

    # 4 - Tab order and no trap
    $order = @(for ($i = 1; $i -le 8; $i++) { Send-ToNoto @() $VK_TAB 400; Focused })
    $inside = 'WelcomeSkipButton', 'WelcomeBackButton', 'WelcomeNextButton'
    $outside = @($order | Where-Object { $_ -notin $inside })
    $skipAt = [array]::IndexOf($order, 'WelcomeSkipButton'); $nextAt = [array]::IndexOf($order, 'WelcomeNextButton')
    Add-Result $run '4  Tab leaves the welcome and comes back (no trap); Skip before Next in reading order' ($outside.Count -ge 1 -and $skipAt -ge 0 -and $nextAt -gt $skipAt -and 'WelcomeBackButton' -notin $order) "tab: $($order -join ' > ')"
    # Back to Next, from wherever Tab left focus.
    (Find-ById 'WelcomeNextButton').SetFocus(); Start-Sleep -Milliseconds 300

    # 5 - Get started
    foreach ($i in 1..3) { Send-ToNoto @() $VK_ENTER }
    $last = Step
    Send-ToNoto @() $VK_ENTER 1200
    $focus = Focused
    Add-Result $run '5  Get started closes it to the folder list, focus on the name box; recorded Completed' ($last.Next -eq 'Get started' -and (Gone 'WelcomeRoot') -and ((Shown 'FolderList') -or (Shown 'FolderNameInput')) -and $focus -eq 'FolderNameInput' -and (State $a) -eq 'Completed') "last='$($last.Next)' focus='$focus' state='$(State $a)'"
    Stop-Clean

    # 6 - not again
    $null = Start-On $a -Keep
    Bring-Forward
    Add-Result $run '6  a completed welcome is not shown at the next start' (-not (Shown 'WelcomeRoot') -and (State $a) -eq 'Completed') "welcome=$(Shown 'WelcomeRoot') state='$(State $a)'"

    # 7 - from Settings > Help, and Escape back to Settings
    Send-ToNoto @($VK_CTRL) $VK_COMMA 1400
    $null = Find-ById 'SettingsRoot'
    # Help is the page's last group: scrolled to, so its row is on screen.
    (Find-ById 'SettingsScroll').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 600
    (Find-ById 'ShowWelcomeButton').SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() $VK_ENTER 1200
    $s = Step; $focusIn = Focused
    Send-ToNoto @() $VK_ESC 1200
    $focusBack = Focused
    Add-Result $run '7  Settings > Help > Show tour opens it at step 1; Escape goes back to Settings, focus on Show tour' ($s.Progress -eq 'Step 1 of 4' -and $s.Title -eq $Titles[0] -and $focusIn -eq 'WelcomeNextButton' -and (Gone 'WelcomeRoot') -and (Shown 'SettingsRoot') -and $focusBack -eq 'ShowWelcomeButton') "opened at '$($s.Progress)' focus '$focusIn'; after Escape settings=$(Shown 'SettingsRoot') focus '$focusBack'"
    $shotSettings = Shot 'settings-help'
    Send-ToNoto @() $VK_ESC 900
    Stop-Clean

    # 8 - Skip, pressed as a pointer would
    $b = New-Root 'b'
    $null = Start-On $b -Keep
    (Find-ById 'WelcomeSkipButton').GetCurrentPattern($Invoke).Invoke(); Start-Sleep -Milliseconds 1000
    $closed = Gone 'WelcomeRoot'; $state = State $b
    Stop-Clean
    $again = @(foreach ($i in 1..2) { $null = Start-On $b -Keep; Start-Sleep -Milliseconds 800; Shown 'WelcomeRoot'; Stop-Clean })
    Add-Result $run '8  Skip closes it; recorded Skipped; two more starts never show it' ($closed -and $state -eq 'Skipped' -and @($again | Where-Object { $_ }).Count -eq 0) "closed=$closed state='$state' later starts showed=$($again -join ',')"

    # 9 - Escape
    $c = New-Root 'c'
    $null = Start-On $c -Keep
    Bring-Forward
    Send-ToNoto @() $VK_ESC 1200
    Add-Result $run '9  Escape closes it; recorded Skipped' ((Gone 'WelcomeRoot') -and (State $c) -eq 'Skipped' -and (Shown 'FolderNameInput')) "state='$(State $c)' focus='$(Focused)'"
    Stop-Clean

    # 10 - killed with the welcome open
    $d = New-Root 'd'
    $null = Start-On $d -Keep
    $null = Find-ById 'WelcomeRoot'
    Stop-Process -Id $script:noto.Pid -Force; Start-Sleep -Milliseconds 800
    $null = Start-On $d -Keep
    $shown = [bool](Find-ById 'WelcomeRoot'); $s = Step
    Add-Result $run '10 a start killed with the welcome open: the next start shows it again, still owed' ($shown -and $s.Progress -eq 'Step 1 of 4' -and (State $d) -eq 'Due') "shown=$shown at '$($s.Progress)' state='$(State $d)'"
    Stop-Clean

    # 11 - existing workspaces from a Noto without the welcome
    $results = foreach ($withFolder in $true, $false) {
        $e = New-Root ("e-$withFolder")
        $null = Start-On $e; Stop-Clean   # created (its welcome skipped by Start-Noto), then made a pre-welcome workspace
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $e -Sql "DELETE FROM Settings WHERE Key = 'onboarding.welcome'"
        if ($withFolder) {
            $now = [DateTimeOffset]::UtcNow.ToString('O')
            $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $e -Sql 'INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ($i, ''Mine'', 1, $t, $t)' -Parameters @{ '$i' = '01J0000000000000000000F0A1'; '$t' = $now }
        }
        $null = Start-On $e -Keep; Start-Sleep -Milliseconds 1000
        $seen = Shown 'WelcomeRoot'; $row = State $e
        Stop-Clean
        "folder=$withFolder welcome=$seen row='$row'"
    }
    Add-Result $run '11 an existing workspace without the welcome row never shows it: with a folder, and empty' (@($results | Where-Object { $_ -notlike '*welcome=False row=''''' }).Count -eq 0) ($results -join ' | ')

    # 12 - Light and Dark, every step
    $shots = @(); $fits = @()
    foreach ($theme in 'Light', 'Dark') {
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $d -Key 'appearance.theme' -Value $theme
        $null = Start-On $d -Keep
        Bring-Forward
        for ($i = 0; $i -lt 4; $i++) {
            if ($i -gt 0) { Send-ToNoto @() $VK_ENTER 900 }
            $shots += Shot "$($theme.ToLower())-step$($i + 1)"
            $fits += Fits
        }
        Stop-Clean   # a clean close with the welcome open: still owed, so the next theme's start shows it again
    }
    Add-Result $run '12 Light and Dark: every step captured for review; the column and its actions fit inside the window' ($shots.Count -eq 8 -and @($fits | Where-Object { $_ -ne 'ok' }).Count -eq 0 -and (State $d) -eq 'Due') "$($shots -join ' ') $shotSettings; fits=$($fits -join ',')"

    # 13 - clean ends
    Add-Result $run '13 every start in the run ended cleanly (no crash)' ($script:unclean -eq 0 -and $script:clean -ge 12) "clean=$($script:clean) unclean=$($script:unclean) (one start in case 10 was killed on purpose)"
}) }
finally {
    $script:log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($script:log.All())
}
