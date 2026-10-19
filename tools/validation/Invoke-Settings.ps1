#Requires -Version 7.4
<#
.SYNOPSIS
  Slice 1 settings: the settings surface, driven through the real UI, with each change's live effect, persistence
  across a restart, the corrupt-value fallback and the keyboard-only path.

.DESCRIPTION
  One isolated data root seeded with a folder holding one note. Noto, the target app and every helper are started
  by the shell; the run happens on a temporary virtual desktop and the real data folders are checked unchanged.
  Settings are changed through UI Automation (Toggle, Selection) or real keys, and keys and clicks reach Noto only
  while it verifiably owns the foreground and the point. Every foreground claim is bracketed by negative controls; a
  grant makes the run INVALID. Evidence: UI Automation state, the window's own geometry, the settings rows in the
  isolated database, Noto's trace (Noto.Settings / Noto.Workspace / Noto.Hotkey / Noto.Login lines, never values a
  user typed), and a screenshot per theme for the visual review.

    1  entry: Ctrl+, from the folder list opens Settings with focus inside it; Escape (default) goes back, focus
       restored; the header button opens it and Back closes it; from the note list and from the editor it returns to
       the same surface, the editor's unsaved text intact (Ctrl+, from inside the editor's text opens it once, and
       the editor's typing, selection and Ctrl+W are unchanged); through each entry, twice, UI Automation has one
       SettingsRoot named Settings holding each control once, focus on Back, and after each close none of it, the
       held element offscreen, the next open a new element
    2  keyboard only: Tab visits every control in visual order and cycles back; Space toggles; Alt+Down, Down,
       Enter changes a ComboBox; Escape leaves
    3  theme: Dark, Light, then System, each applied at once (the framework's ActualTheme), stored by name;
       a screenshot of each
    4  side: Left re-docks the shown drawer at once (flush with the work area's left edge), Right back
    5  Escape key: Do nothing keeps Settings; Hide Noto hides; Go back only leaves Settings and then does nothing at
       the folder list; the default restored
    6  hide when I click elsewhere: off -> activating another app leaves Noto shown; on -> it hides
    7  single click: on -> one click enters a folder and one opens a note, and a double click opens no note it was
       not aimed at; off -> one click only selects, a double click enters
    8  shortcut: off -> the chord is released (this process can register it); on -> held again; with another process
       holding it, turning it on is refused with an announced message and the stored choice kept
    9  Start with Windows reflects Windows' own registration (read-only). With -RealRegistry, the one approved round
       trip: on writes exactly "<this exe>" --login, off removes it; Run and StartupApproved restored and compared
    10 data folder: Open in Explorer opens an Explorer window on the isolated data root (closed again)
    11 restart: every changed setting survives a restart and is in effect from the first frame
    12 corrupt rows: an unreadable theme and click setting read as their defaults and are left as they were
    13 the isolated database holds no setting key Noto does not declare
    14 rebinding (Slice 1.2b): the current chord works; the recorder starts and suspends it (a press is ignored, the
       page stays); a new chord registers, the old is released, and it is stored; the new chord shows Noto; a chord
       another process legitimately holds is refused, announced, nothing changes, and the current chord still works;
       Escape and losing activation cancel and give the chord back; a restart keeps the new binding; the default is
       restored through the recorder

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Settings.ps1 -NotoExe <frozen Release>\Noto.exe
  pwsh -NoProfile -File tools/validation/Invoke-InShell.ps1 -NotoExe <path>\Noto.exe Invoke-Settings -RealRegistry
#>
param([Parameter(Mandatory)] [string] $NotoExe, [switch] $RealRegistry, [string[]] $Only = @())
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'settings'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F0S1'
$NoteId = '01J0000000000000000000N0S1'
$RunPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$ApprovedPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$Declared = @('activation.hotkey.enabled', 'activation.hotkey.binding', 'workspace.edge', 'workspace.width', 'workspace.escape', 'workspace.hide-on-deactivation', 'workspace.single-click-open', 'appearance.theme')

# ---------------------------------------------------------------- helpers

function Want([string] $Id) { $Only.Count -eq 0 -or $Only -contains $Id }
function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Ev { Get-WindowEvidence $noto }
function Trace-Mark { @($log.Lines($noto.Pid)).Count }
function Trace-Since([int] $Mark) { @($log.Lines($noto.Pid) | Select-Object -Skip $Mark) -join ' | ' }
function Wait-Trace([int] $Mark, [string] $Pattern, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if ((Trace-Since $Mark) -match $Pattern) { return $true }; Start-Sleep -Milliseconds 100 }
    $false
}
function Until([scriptblock] $Condition, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 100 }
    [bool](& $Condition)
}
function Element([string] $Id) { $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Visible-Id([string] $Id) { $e = Element $Id; [bool]($e -and -not $e.Current.IsOffscreen) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    if (-not (Until { Visible-Id $Id } $Seconds)) { throw "no visible element '$Id' within ${Seconds}s" }
    Element $Id
}
function Items([string] $ListId) {
    @((Find-ById $ListId).FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
}
function Count-In($Under, [string] $Id) { @($Under.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id))).Count }
$SettingsControls = @('SettingsBackButton', 'LaunchAtLoginSetting', 'HideOnDeactivationSetting', 'SingleClickSetting', 'SideSetting', 'EscapeSetting', 'ThemeSetting', 'HotkeyEnabledSetting', 'RevealDataFolderButton')
function Settings-Wrong {
    <# What is wrong with the open page as UI Automation has it: one SettingsRoot named Settings, each control exactly once in the window and inside it. Empty when nothing. #>
    $window = $AE::FromHandle($noto.Hwnd)
    $roots = Count-In $window 'SettingsRoot'
    if ($roots -ne 1) { return "SettingsRoot x$roots" }
    $root = Element 'SettingsRoot'
    if ($root.Current.Name -ne 'Settings') { return "SettingsRoot named '$($root.Current.Name)'" }
    @($SettingsControls | Where-Object { (Count-In $window $_) -ne 1 -or (Count-In $root $_) -ne 1 } | ForEach-Object { "$_ not once inside" }) -join ', '
}
function Settings-Focusable {
    <# Every control on the open page a keyboard can reach, by id, as UI Automation reports it: what the Tab sequence must name. #>
    $condition = [System.Windows.Automation.PropertyCondition]::new($AE::IsKeyboardFocusableProperty, $true)
    @((Element 'SettingsRoot').FindAll($Scope::Descendants, $condition) | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ } | Sort-Object -Unique)
}
function Explorer-Windows([string] $Uri, [switch] $Close) {
    <# The Explorer windows on $Uri, counted (and closed) by a child process, never through this process's COM (helpers/explorer-windows.ps1). #>
    $out = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'helpers/explorer-windows.ps1') -Uri $Uri @(if ($Close) { '-Close' })
    if ($LASTEXITCODE -ne 0) { throw "explorer-windows.ps1 failed: $out" }
    [int]@($out)[-1]
}
function Settings-Left { @(@('SettingsRoot') + $SettingsControls | Where-Object { (Count-In $AE::FromHandle($noto.Hwnd) $_) -ne 0 }) -join ', ' }
function Focused { $f = $AE::FocusedElement; "$($f.Current.ControlType.ProgrammaticName)|id=$($f.Current.AutomationId)|name=$($f.Current.Name)" }
function Focused-Id { $AE::FocusedElement.Current.AutomationId }
function Is-On([string] $Id) { (Find-ById $Id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' }
function Is-Enabled([string] $Id) { (Find-ById $Id).Current.IsEnabled }
function Toggle([string] $Id) { (Find-ById $Id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 400 }
function Set-Toggle([string] $Id, [bool] $On) { if ((Is-On $Id) -ne $On) { Toggle $Id }; if ((Is-On $Id) -ne $On) { throw "$Id did not turn $(if ($On) { 'on' } else { 'off' })" } }
function Combo-Value([string] $Id) {
    $selection = (Find-ById $Id).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count) { $selection[0].Current.Name } else { '' }
}
function Choose([string] $Id, [string] $Name) {
    <# Through UI Automation: expand, select the item by its name, collapse. No key or click involved. #>
    $box = Find-ById $Id
    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
    $item = $box.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, $Name))
    if (-not $item) { $item = $AE::RootElement.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, $Name), [System.Windows.Automation.PropertyCondition]::new($AE::ProcessIdProperty, $noto.Pid))) }
    if (-not $item) { throw "no item '$Name' in $Id" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
    try { $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Start-Sleep -Milliseconds 400
    if ((Combo-Value $Id) -ne $Name) { throw "$Id did not change to '$Name' (it reads '$(Combo-Value $Id)')" }
}
function Stored([string] $Key) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql 'SELECT Value FROM Settings WHERE Key = $k' -Parameters @{ '$k' = $Key } }
function Send-ToNoto([int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $noto.Hwnd) { throw 'refusing keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 600
}
function Ctrl([string] $Label, [IntPtr] $Away) {
    $neg = Test-NegativeControl $run $Away
    $run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $run "$Label negative control was granted the foreground: $($neg.Detail)" }
}
function Noto-Front([string] $Label) {
    <# The real path: a click into the target app, then the activation chord, which brings a shown Noto forward or shows a hidden one. #>
    if ((Fg) -eq $noto.Hwnd) { return }
    $null = Set-ForegroundByKeyboard $target
    Ctrl "$Label before" $noto.Hwnd
    Send-ActivationChord -AllowedForeground $target.Hwnd
    if (-not (Until { (Fg) -eq $noto.Hwnd } 5)) { throw "$Label - the chord did not bring Noto forward" }
    Ctrl "$Label after" $target.Hwnd
}
function Click-At([int] $X, [int] $Y, [switch] $Double) {
    <# A real click, only where the root window under the point is Noto. #>
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $X; $pt.Y = $Y
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $noto.Hwnd) { throw "refusing to click at ($X,$Y): it is not Noto" }
    [void][NotoVal.Win32]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 150
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
    if ($Double) { Start-Sleep -Milliseconds 60; [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 900
}
function Centre($Element) { $r = $Element.Current.BoundingRectangle; @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) }
function Open-Settings-ByKeys { Send-ToNoto @(0x11) 0xBC; $null = Find-ById 'SettingsRoot' }
function Close-Settings-ByBack { (Find-ById 'SettingsBackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); if (-not (Until { -not (Visible-Id 'SettingsRoot') })) { throw 'Back did not leave Settings' } }
function To-FolderList {
    if (Visible-Id 'SettingsRoot') { Close-Settings-ByBack }
    if (Visible-Id 'NoteEditor') { (Find-ById 'EditorBackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500 }
    if (Visible-Id 'BackButton') { (Find-ById 'BackButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500 }
    $null = Find-ById 'FolderList'
}
function Open-Settings-ByButton { (Find-ById 'SettingsButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $null = Find-ById 'SettingsRoot' }
function Windows-AppTheme {
    $v = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -ErrorAction SilentlyContinue
    if ($null -eq $v -or $v.AppsUseLightTheme -ne 0) { 'Light' } else { 'Dark' }
}
function High-Contrast { $f = (Get-ItemProperty 'HKCU:\Control Panel\Accessibility\HighContrast' -Name Flags -ErrorAction SilentlyContinue).Flags; [bool]($f -and ([int]$f -band 1)) }
function Screenshot([string] $Name) {
    $r = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($noto.Hwnd, [ref]$r)
    $bmp = [System.Drawing.Bitmap]::new([Math]::Max(1, $r.Right - $r.Left), [Math]::Max(1, $r.Bottom - $r.Top))
    try { $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size); $g.Dispose(); $path = Join-Path $run.Root "$Name.png"; $bmp.Save($path); $path } finally { $bmp.Dispose() }
}
function Registry-Snapshot([string] $Path) {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Path, $false)
    if (-not $k) { return [ordered]@{ '<key>' = 'absent' } }
    try {
        $m = [ordered]@{}
        foreach ($v in ($k.GetValueNames() | Sort-Object)) { $d = $k.GetValue($v, $null, 'DoNotExpandEnvironmentNames'); $m[$v] = "$($k.GetValueKind($v))|$(if ($d -is [byte[]]) { [Convert]::ToHexString($d) } else { "$d" })" }
        $m
    } finally { $k.Dispose() }
}
function Same($A, $B) { (($A.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n") -eq (($B.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n") }
function Login-Expected {
    <# What Windows has for this exe, read as Noto reads it (LoginRegistration): On, Off or WindowsDisabled. #>
    $run1 = Registry-Snapshot $RunPath
    if (-not $run1.Contains('Noto') -or $run1['Noto'] -ne "String|`"$NotoExe`" --login") { return 'Off' }
    $approved = Registry-Snapshot $ApprovedPath
    if (-not $approved.Contains('Noto')) { return 'On' }
    if ($approved['Noto'] -match '^Binary\|02[0-9A-F]{22}$') { return 'On' }
    'WindowsDisabled'
}
# Rebinding (case 14): chords sent and probed by virtual key, Ctrl+Alt+Win held (MOD_CONTROL|MOD_ALT|MOD_WIN).
$VkSpace = 0x20; $VkNew = 0x78; $VkHeld = 0x79   # Space (the default), F9 (the new chord), F10 (held by another process)
function Chord-Free([uint32] $Vk) {
    <# Can this process register Ctrl+Alt+Win+<Vk> right now: true means nothing holds it, Noto included. #>
    $ok = [NotoVal.Win32]::RegisterHotKey([IntPtr]::Zero, 0x5651, 0x400B, $Vk)
    if ($ok) { [void][NotoVal.Win32]::UnregisterHotKey([IntPtr]::Zero, 0x5651) }
    $ok
}
function Send-Chord([IntPtr] $Allowed, [int] $Vk) {
    <# Ctrl+Alt+Win+<Vk>, only while the window it is meant for is in front. #>
    if ((Fg) -ne $Allowed) { throw 'refusing to send a chord: the foreground window is not the intended one' }
    $down = @($VK.CTRL, $VK.ALT, $VK.LWIN) | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }
    $up = @($VK.LWIN, $VK.ALT, $VK.CTRL) | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) }
    [NotoVal.Win32]::Keys(@($down) + @([NotoVal.Win32]::Vk($Vk, $false), [NotoVal.Win32]::Vk($Vk, $true)) + @($up))
    Start-Sleep -Milliseconds 1000
}
function Shown-By([int] $Vk, [string] $Label) {
    <# From the target app in front, the chord brings Noto in front: the chord works. #>
    $null = Set-ForegroundByKeyboard $target
    Ctrl "$Label before" $noto.Hwnd
    Send-Chord $target.Hwnd $Vk
    $ok = Until { (Fg) -eq $noto.Hwnd } 5
    if ($ok) { Ctrl "$Label after" $target.Hwnd }
    $ok
}
function Recorder-Status { $e = Element 'HotkeyRecorderStatus'; if ($e -and -not $e.Current.IsOffscreen) { $e.Current.Name } else { '' } }
function Recorder-Start {
    if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
    (Find-ById 'HotkeyRecorder').SetFocus(); Start-Sleep -Milliseconds 200
    Send-ToNoto @() 0x20   # Space selects the recorder
    if (-not (Until { (Recorder-Status) -match '^Recording' })) { throw "the recorder did not start: '$(Recorder-Status)'" }
}
function Start-ChordHolder([uint32] $Vk) {
    <# Another process legitimately holding Ctrl+Alt+Win+<Vk> (helpers/hotkey-holder.ps1). #>
    $ready = Join-Path $run.Root "holder-$Vk-ready.txt"; $stop = Join-Path $run.Root "holder-$Vk.stop"
    $null = Start-ViaShell $run -File 'pwsh.exe' -Arguments "-NoProfile -WindowStyle Hidden -File `"$(Join-Path $PSScriptRoot 'helpers/hotkey-holder.ps1')`" -ReadyFile `"$ready`" -StopFile `"$stop`" -VirtualKey $Vk" -Marker $ready -Show 0
    if (-not (Until { Test-Path $ready } 30)) { throw 'the chord holder did not start' }
    if ((Get-Content $ready -Raw) -notmatch 'held=True') { throw "the chord holder could not take the chord: $(Get-Content $ready -Raw)" }
    [pscustomobject]@{ Ready = $ready; Stop = $stop }
}

function Restart-Noto {
    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly before a restart' }
    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
}

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title 'Slice 1: the settings surface' -Body {
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        if (High-Contrast) { Add-Invalid $run 'high contrast is on: the theme cases measure Light and Dark, which high contrast overrides'; return }

        $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
        if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Validation', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, 'Settings validation note', 1, `$t, `$t)" -Parameters @{ '$n' = $NoteId; '$f' = $FolderId; '$t' = $now }
        $script:target = Start-TargetApp $run
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        Noto-Front 'start'

        # ------------------------------------------------ 1 entry, back, focus restored
        if (Want 1) {
            To-FolderList
            $row = (Items 'FolderList')[0]; $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus(); Start-Sleep -Milliseconds 300
            $before = Focused
            $m = Trace-Mark; Open-Settings-ByKeys
            $inside = Until { (Focused-Id) -in @('SettingsBackButton', 'LaunchAtLoginSetting', 'HideOnDeactivationSetting') }
            Add-Result $run '1  Ctrl+, on the folder list opens Settings, focus inside it' ((Visible-Id 'SettingsRoot') -and -not (Visible-Id 'FolderList') -and $inside -and (Trace-Since $m) -match 'Noto\.Settings opened') "focus=$(Focused); trace: $(Trace-Since $m)"
            Send-ToNoto @() 0x1B
            $back = Until { (Visible-Id 'FolderList') -and -not (Visible-Id 'SettingsRoot') }
            $restored = Until { (Focused) -eq $before }
            Add-Result $run '1  Escape (default) goes back to the folder list, focus restored' ($back -and $restored) "before=$before after=$(Focused)"

            Open-Settings-ByButton
            Add-Result $run '1  the header Settings button opens it' (Visible-Id 'SettingsRoot') ''
            Close-Settings-ByBack
            Add-Result $run '1  Back closes it, back to the folder list' ((Visible-Id 'FolderList') -and -not (Visible-Id 'SettingsRoot')) ''

            # The page itself is the element UI Automation and a screen reader find (measured: an id on its inner grid
            # never reached the tree). Each open builds a new page and each close removes all of it.
            $previous = $null
            foreach ($entry in 'Ctrl+,', 'header', 'Ctrl+,', 'header') {
                if ($entry -eq 'header') { Open-Settings-ByButton } else { Open-Settings-ByKeys }
                $root = Element 'SettingsRoot'
                $wrong = Settings-Wrong
                $fresh = $null -eq $previous -or -not [System.Windows.Automation.Automation]::Compare($root, $previous)
                $focus = Until { (Focused-Id) -eq 'SettingsBackButton' }
                Add-Result $run "1  $entry opens one SettingsRoot named Settings, each control once inside it, a new element, focus on Back" (-not $wrong -and $fresh -and $focus) "wrong=[$wrong] fresh=$fresh focus=$(Focused)"
                Send-ToNoto @() 0x1B
                $gone = Until { -not (Settings-Left) }
                Add-Result $run '1  closing removes the page and every control from UI Automation; the held element is offscreen' ($gone -and $root.Current.IsOffscreen -and (Visible-Id 'FolderList')) "left=[$(Settings-Left)]"
                $previous = $root
            }

            $row = (Items 'FolderList')[0]; $row.SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @(0x11) 0x28   # Ctrl+Down: enter the folder
            $null = Find-ById 'NoteList'
            Open-Settings-ByKeys; Send-ToNoto @() 0x1B
            Add-Result $run '1  from the note list, Settings returns to the note list' ((Until { Visible-Id 'NoteList' }) -and -not (Visible-Id 'SettingsRoot')) ''

            $note = (Items 'NoteList')[0]; $note.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $note.SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @() 0x0D       # Enter: open the note
            $editor = Find-ById 'NoteEditor'
            $text = "unsaved $([guid]::NewGuid().ToString('N').Substring(0, 6))"
            $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); $editor.SetFocus(); Start-Sleep -Milliseconds 300
            # Inside the TextBox, which consumes Ctrl+, itself (measured): the editor hands it to the root's accelerator.
            $inEditor = (Focused-Id) -eq 'NoteEditor'
            $m = Trace-Mark; Send-ToNoto @(0x11) 0xBC
            $shown = Until { Visible-Id 'SettingsRoot' }
            $opens = ([regex]::Matches((Trace-Since $m), 'Noto\.Settings opened')).Count
            $wrong = Settings-Wrong
            $focus = Until { (Focused-Id) -eq 'SettingsBackButton' }
            Add-Result $run '1  Ctrl+, with focus in the editor opens Settings once: one page, focus on Back' ($inEditor -and $shown -and $opens -eq 1 -and -not $wrong -and $focus -and -not (Visible-Id 'NoteEditor')) "inEditor=$inEditor opens=$opens wrong=[$wrong] focus=$(Focused); trace: $(Trace-Since $m)"
            if ($shown) { Send-ToNoto @() 0x1B }
            $kept = (Find-ById 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            Add-Result $run '1  from the editor, Settings returns to the editor, text intact, focus in it' ((Until { (Focused-Id) -eq 'NoteEditor' }) -and $kept -eq $text) "focus=$(Focused) intact=$($kept -eq $text)"

            # The editor's own keys are its own: typing, a Shift selection replaced by typing, Backspace.
            Send-ToNoto @(0x11) 0x23; Send-ToNoto @() 0x41; Send-ToNoto @() 0x42; Send-ToNoto @(0x10) 0x25; Send-ToNoto @() 0x43; Send-ToNoto @() 0x08
            $typed = (Find-ById 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            Add-Result $run '1  typing, selecting and deleting in the editor are unchanged' ($typed -eq "${text}a" -and -not (Visible-Id 'SettingsRoot')) "value=[$typed]"

            # Ctrl+W from the editor still hides Noto, by the root's accelerator.
            $m = Trace-Mark; Send-ToNoto @(0x11) 0x57
            $hid = Until { -not (Ev).WsVisible }
            Add-Result $run '1  Ctrl+W with focus in the editor still hides Noto' ($hid -and (Trace-Since $m) -match 'Noto\.Workspace request=Dismiss action=Hide' -and (Trace-Since $m) -notmatch 'Noto\.Settings opened') "trace: $(Trace-Since $m)"
            Noto-Front '1 after Ctrl+W'
            To-FolderList   # leaving the editor saves the text
        }

        # ------------------------------------------------ 2 keyboard only
        if (Want 2) {
            Noto-Front '2'
            To-FolderList; (Items 'FolderList')[0].SetFocus(); Start-Sleep -Milliseconds 200
            Open-Settings-ByKeys
            $null = Until { (Focused-Id) -ne '' }
            $login = Login-Expected
            $expected = @('SettingsBackButton') + $(if ($login -ne 'WindowsDisabled') { @('LaunchAtLoginSetting') } else { @('OpenStartupAppsButton') }) +
                @('HideOnDeactivationSetting', 'SingleClickSetting', 'SideSetting', 'EscapeSetting', 'ThemeSetting', 'HotkeyEnabledSetting', 'RevealDataFolderButton', 'SettingsButton', 'PinToggle', 'SettingsBackButton')
            # The sequence must name every keyboard-reachable control on the page, so a control added to the page without
            # being added here fails by name instead of as a sequence that merely ends early.
            $page = @($expected | Where-Object { $_ -notin 'SettingsButton', 'PinToggle' } | Sort-Object -Unique)
            $reachable = Settings-Focusable
            $unnamed = @($reachable | Where-Object { $_ -notin $page }); $absent = @($page | Where-Object { $_ -notin $reachable })
            Add-Result $run '2  the expected Tab sequence names every keyboard-reachable control on the page' ($unnamed.Count -eq 0 -and $absent.Count -eq 0) "not in the sequence=[$($unnamed -join ',')] not reachable=[$($absent -join ',')]"
            # One press per step: the last returns to the first control, which is the cycle.
            $seen = @(Focused-Id)
            for ($i = 1; $i -lt $expected.Count; $i++) { Send-ToNoto @() 0x09; $seen += Focused-Id }
            Add-Result $run '2  Tab visits every control in visual order and cycles back' ((($seen -join ',') -eq ($expected -join ','))) "seen=$($seen -join ','); expected=$($expected -join ',')"

            (Find-ById 'HideOnDeactivationSetting').SetFocus(); Start-Sleep -Milliseconds 200
            $was = Is-On 'HideOnDeactivationSetting'
            $m = Trace-Mark; Send-ToNoto @() 0x20
            $flipped = (Is-On 'HideOnDeactivationSetting') -ne $was
            Add-Result $run '2  Space toggles the focused switch, stored and applied' ($flipped -and (Stored 'workspace.hide-on-deactivation') -eq "$(-not $was)" -and (Wait-Trace $m 'Noto\.Settings applied key=workspace\.hide-on-deactivation')) "stored=$(Stored 'workspace.hide-on-deactivation'); trace: $(Trace-Since $m)"
            Send-ToNoto @() 0x20   # back

            (Find-ById 'ThemeSetting').SetFocus(); Start-Sleep -Milliseconds 200
            $m = Trace-Mark
            Send-ToNoto @(0x12) 0x28; Send-ToNoto @() 0x28; Send-ToNoto @() 0x0D   # Alt+Down opens, Down, Enter
            Add-Result $run '2  Alt+Down, Down, Enter changes the theme' ((Combo-Value 'ThemeSetting') -eq 'Light' -and (Stored 'appearance.theme') -eq 'Light' -and (Wait-Trace $m 'theme=Light actual=Light')) "value=$(Combo-Value 'ThemeSetting'); trace: $(Trace-Since $m)"
            Choose 'ThemeSetting' 'Use Windows setting'
            (Find-ById 'HideOnDeactivationSetting').SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @() 0x1B
            Add-Result $run '2  Escape leaves Settings by keyboard' ((Until { Visible-Id 'FolderList' }) -and -not (Visible-Id 'SettingsRoot')) ''
        }

        # ------------------------------------------------ 3 theme
        if (Want 3) {
            Noto-Front '3'
            Open-Settings-ByButton
            foreach ($t in @(@{ Name = 'Dark'; Item = 'Dark'; Actual = 'Dark' }, @{ Name = 'Light'; Item = 'Light'; Actual = 'Light' }, @{ Name = 'System'; Item = 'Use Windows setting'; Actual = (Windows-AppTheme) })) {
                $m = Trace-Mark; Choose 'ThemeSetting' $t.Item
                $applied = Wait-Trace $m "Noto\.Settings theme=$($t.Name) actual=$($t.Actual)"
                $shot = Screenshot "theme-$($t.Name)"
                Add-Result $run "3  theme $($t.Name): applied at once (ActualTheme $($t.Actual)), stored by name" ($applied -and (Stored 'appearance.theme') -eq $t.Name) "stored=$(Stored 'appearance.theme') screenshot=$shot; trace: $(Trace-Since $m)"
            }
        }

        # ------------------------------------------------ 4 side
        if (Want 4) {
            Noto-Front '4'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            $m = Trace-Mark; Choose 'SideSetting' 'Left'
            $left = Until { Test-DockedAt (Ev) 'Left' }
            Add-Result $run '4  Left re-docks the shown drawer at once' ($left -and (Stored 'workspace.edge') -eq 'Left' -and (Wait-Trace $m 'Noto\.Workspace edge=Left redocked=True')) "frame=$((Ev).Frame) work=$((Ev).Work); trace: $(Trace-Since $m)"
            $m = Trace-Mark; Choose 'SideSetting' 'Right'
            Add-Result $run '4  Right re-docks it back' ((Until { Test-DockedAt (Ev) 'Right' }) -and (Stored 'workspace.edge') -eq 'Right') "frame=$((Ev).Frame); trace: $(Trace-Since $m)"
        }

        # ------------------------------------------------ 5 Escape key
        if (Want 5) {
            Noto-Front '5'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            Choose 'EscapeSetting' 'Do nothing'
            (Find-ById 'HotkeyEnabledSetting').SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @() 0x1B
            Add-Result $run '5  Do nothing: Escape leaves Settings open' ((Visible-Id 'SettingsRoot') -and (Ev).WsVisible -and (Stored 'workspace.escape') -eq 'None') ''

            Choose 'EscapeSetting' 'Hide Noto'
            (Find-ById 'HotkeyEnabledSetting').SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @() 0x1B
            Add-Result $run '5  Hide Noto: Escape hides the workspace' (Until { -not (Ev).WsVisible }) ''
            Noto-Front '5 re-show'

            Choose 'EscapeSetting' 'Go back only'
            (Find-ById 'HotkeyEnabledSetting').SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @() 0x1B
            $atList = Until { Visible-Id 'FolderList' }
            Send-ToNoto @() 0x1B
            Add-Result $run '5  Go back only: Escape leaves Settings, then does nothing at the folder list' ($atList -and (Ev).WsVisible -and (Visible-Id 'FolderList')) ''

            Open-Settings-ByButton; Choose 'EscapeSetting' 'Go back, then hide'
            Add-Result $run '5  the default is restored' ((Stored 'workspace.escape') -eq 'LeaveFolderOrHide') ''
        }

        # ------------------------------------------------ 6 hide when I click elsewhere
        if (Want 6) {
            Noto-Front '6'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            Set-Toggle 'HideOnDeactivationSetting' $false
            $null = Set-ForegroundByKeyboard $target
            Start-Sleep -Milliseconds 800
            Add-Result $run '6  off: activating another app leaves Noto shown' ((Ev).WsVisible -and (Stored 'workspace.hide-on-deactivation') -eq 'False') "$((Ev).WsVisible)"
            Noto-Front '6 front'
            Set-Toggle 'HideOnDeactivationSetting' $true
            $null = Set-ForegroundByKeyboard $target
            Add-Result $run '6  on: activating another app hides Noto' ((Until { -not (Ev).WsVisible }) -and (Stored 'workspace.hide-on-deactivation') -eq 'True') ''
            Noto-Front '6 re-show'
        }

        # ------------------------------------------------ 7 single click
        if (Want 7) {
            Noto-Front '7'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            Set-Toggle 'SingleClickSetting' $true
            Close-Settings-ByBack
            $xy = Centre (Items 'FolderList')[0]; Click-At $xy[0] $xy[1]
            $entered = Until { Visible-Id 'NoteList' }
            $xy = Centre (Items 'NoteList')[0]; Click-At $xy[0] $xy[1]
            $opened = Until { Visible-Id 'NoteEditor' }
            Add-Result $run '7  on: one click enters a folder, one click opens a note' ($entered -and $opened -and (Stored 'workspace.single-click-open') -eq 'True') "entered=$entered opened=$opened"
            To-FolderList
            $xy = Centre (Items 'FolderList')[0]; Click-At $xy[0] $xy[1] -Double
            Add-Result $run '7  on: a double click enters the folder and opens no note it was not aimed at' ((Until { Visible-Id 'NoteList' }) -and -not (Visible-Id 'NoteEditor')) ''

            To-FolderList; Open-Settings-ByButton; Set-Toggle 'SingleClickSetting' $false; Close-Settings-ByBack
            $xy = Centre (Items 'FolderList')[0]; Click-At $xy[0] $xy[1]
            $stayed = -not (Until { Visible-Id 'NoteList' } 1)
            Click-At $xy[0] $xy[1] -Double
            Add-Result $run '7  off: one click only selects, a double click enters' ($stayed -and (Until { Visible-Id 'NoteList' }) -and (Stored 'workspace.single-click-open') -eq 'False') "stayed=$stayed"
            To-FolderList
        }

        # ------------------------------------------------ 8 shortcut
        if (Want 8) {
            Noto-Front '8'
            Open-Settings-ByButton
            $m = Trace-Mark; Set-Toggle 'HotkeyEnabledSetting' $false
            Add-Result $run '8  off: the chord is released' ((Wait-Trace $m 'Noto\.Hotkey enabled=False registered=False') -and (Test-ChordFree) -and (Stored 'activation.hotkey.enabled') -eq 'False') "trace: $(Trace-Since $m)"
            $m = Trace-Mark; Set-Toggle 'HotkeyEnabledSetting' $true
            Add-Result $run '8  on: the chord is held again' ((Wait-Trace $m 'Noto\.Hotkey enabled=True registered=True') -and -not (Test-ChordFree) -and -not (Visible-Id 'HotkeyMessage')) "trace: $(Trace-Since $m)"

            Set-Toggle 'HotkeyEnabledSetting' $false
            $holder = Start-HotkeyHolder $run
            try {
                $m = Trace-Mark; Set-Toggle 'HotkeyEnabledSetting' $true
                $message = if (Until { Visible-Id 'HotkeyMessage' }) { (Element 'HotkeyMessage').Current.Name } else { '' }
                Add-Result $run '8  held by another process: refused, announced, the choice kept' ((Wait-Trace $m 'Noto\.Hotkey enabled=True registered=False') -and $message -match 'in use by another app' -and (Stored 'activation.hotkey.enabled') -eq 'True') "message='$message'; trace: $(Trace-Since $m)"
            }
            finally { $null = Stop-HotkeyHolder $holder }
            $m = Trace-Mark; Set-Toggle 'HotkeyEnabledSetting' $false; Set-Toggle 'HotkeyEnabledSetting' $true
            Add-Result $run '8  freed: off and on registers it, the message gone' ((Wait-Trace $m 'Noto\.Hotkey enabled=True registered=True') -and -not (Visible-Id 'HotkeyMessage')) "trace: $(Trace-Since $m)"
        }

        # ------------------------------------------------ 9 Start with Windows
        if (Want 9) {
            Noto-Front '9'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            $expected = Login-Expected
            Add-Result $run '9  Start with Windows shows Windows'' own registration' ((Is-On 'LaunchAtLoginSetting') -eq ($expected -eq 'On') -and (Is-Enabled 'LaunchAtLoginSetting') -eq ($expected -ne 'WindowsDisabled')) "windows=$expected on=$(Is-On 'LaunchAtLoginSetting') enabled=$(Is-Enabled 'LaunchAtLoginSetting')"

            if ($RealRegistry) {
                $runBefore = Registry-Snapshot $RunPath; $approvedBefore = Registry-Snapshot $ApprovedPath
                if ($runBefore.Contains('Noto')) { throw 'a Run\Noto value already exists; refusing to touch it' }
                try {
                    $m = Trace-Mark; Set-Toggle 'LaunchAtLoginSetting' $true
                    $written = Wait-Trace $m 'Noto\.Login before=Off after=On source=settings' 10
                    $value = (Registry-Snapshot $RunPath)['Noto']
                    if ($written -and -not $value) { Add-Invalid $run '9  Noto wrote Run\Noto but this process cannot see it: HKCU is virtualized here (run through Invoke-InShell)' }
                    Add-Result $run '9  on writes exactly "<this exe>" --login' ($written -and $value -eq "String|`"$NotoExe`" --login") "value=$value; trace: $(Trace-Since $m)"
                    $m = Trace-Mark; Set-Toggle 'LaunchAtLoginSetting' $false
                    Add-Result $run '9  off removes it' ((Wait-Trace $m 'Noto\.Login before=On after=Off source=settings' 10) -and -not (Registry-Snapshot $RunPath).Contains('Noto')) "trace: $(Trace-Since $m)"
                    Add-Result $run '9  Startup apps byte-identical throughout (Noto never writes it)' (Same $approvedBefore (Registry-Snapshot $ApprovedPath)) ''
                }
                finally {
                    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($RunPath, $true)
                    try { if (-not $runBefore.Contains('Noto')) { $k.DeleteValue('Noto', $false) } } finally { $k.Dispose() }
                    Add-Result $run '9  restored: Run and StartupApproved identical to the snapshot' ((Same $runBefore (Registry-Snapshot $RunPath)) -and (Same $approvedBefore (Registry-Snapshot $ApprovedPath))) ''
                }
            }
        }

        # ------------------------------------------------ 10 data folder
        if (Want 10) {
            Noto-Front '10'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            $uri = ([Uri]::new($data)).AbsoluteUri.TrimEnd('/')
            $existing = Explorer-Windows $uri
            (Find-ById 'RevealDataFolderButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $opened = Until { (Explorer-Windows $uri) -gt $existing } 15
            Add-Result $run '10 Open in Explorer opens the isolated data root' $opened "uri=$uri"
            $null = Explorer-Windows $uri -Close
            Start-Sleep -Milliseconds 800
            # Explorer was automated from another process, so this one can still launch through the shell (case 11 restarts).
            $launch = try { [bool][NotoVal.ShellLaunch]::DesktopViewDispatch() } catch { $false }
            Add-Result $run '10 the shell launch path still works after Explorer was automated' $launch ''
            Noto-Front '10 back'
        }

        # ------------------------------------------------ 11 restart
        if (Want 11) {
            Noto-Front '11'
            if (-not (Visible-Id 'SettingsRoot')) { Open-Settings-ByButton }
            Choose 'ThemeSetting' 'Dark'; Choose 'SideSetting' 'Left'; Choose 'EscapeSetting' 'Go back only'
            Set-Toggle 'SingleClickSetting' $true; Set-Toggle 'HideOnDeactivationSetting' $false; Set-Toggle 'HotkeyEnabledSetting' $false
            Restart-Noto
            $mark = 0
            $docked = Until { Test-DockedAt (Ev) 'Left' }
            $themed = (Trace-Since $mark) -match 'Noto\.Settings theme=Dark actual=Dark'
            $free = Test-ChordFree
            Add-Result $run '11 after a restart: docked Left, Dark from the first frame, the chord not held' ($docked -and $themed -and $free) "frame=$((Ev).Frame) trace: $(Trace-Since $mark)"
            # Read through UI Automation, which needs no foreground: the chord is off, so it cannot bring Noto forward.
            Open-Settings-ByButton
            Add-Result $run '11 Settings shows every stored value' ((Combo-Value 'ThemeSetting') -eq 'Dark' -and (Combo-Value 'SideSetting') -eq 'Left' -and (Combo-Value 'EscapeSetting') -eq 'Go back only' -and (Is-On 'SingleClickSetting') -and -not (Is-On 'HideOnDeactivationSetting') -and -not (Is-On 'HotkeyEnabledSetting')) ''
            Choose 'ThemeSetting' 'Use Windows setting'; Choose 'SideSetting' 'Right'; Choose 'EscapeSetting' 'Go back, then hide'
            Set-Toggle 'SingleClickSetting' $false; Set-Toggle 'HideOnDeactivationSetting' $true; Set-Toggle 'HotkeyEnabledSetting' $true
        }

        # ------------------------------------------------ 12 corrupt rows
        if (Want 12) {
            if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
            Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'appearance.theme' -Value 'Purple'
            Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'workspace.single-click-open' -Value 'perhaps'
            $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
            $themed = Until { (Trace-Since 0) -match 'Noto\.Settings theme=System' }
            Noto-Front '12'
            Open-Settings-ByKeys
            Add-Result $run '12 unreadable rows read as their defaults and are left as they were' ($themed -and (Combo-Value 'ThemeSetting') -eq 'Use Windows setting' -and -not (Is-On 'SingleClickSetting') -and (Stored 'appearance.theme') -eq 'Purple' -and (Stored 'workspace.single-click-open') -eq 'perhaps') "theme=$(Combo-Value 'ThemeSetting') stored=$(Stored 'appearance.theme')"
            Choose 'ThemeSetting' 'Light'; Choose 'ThemeSetting' 'Use Windows setting'
            Set-Toggle 'SingleClickSetting' $true; Set-Toggle 'SingleClickSetting' $false
            Add-Result $run '12 choosing a value replaces the unreadable row' ((Stored 'appearance.theme') -eq 'System' -and (Stored 'workspace.single-click-open') -eq 'False') ''
        }

        # ------------------------------------------------ 14 rebinding
        if (Want 14) {
            if (-not (Chord-Free $VkNew) -or -not (Chord-Free $VkHeld)) { Add-Invalid $run '14 Ctrl+Alt+Win+F9 or F10 is already held on this machine'; return }
            Noto-Front '14'
            Add-Result $run '14 the current chord works' ((Shown-By $VkSpace '14 current') -and -not (Chord-Free $VkSpace)) ''

            Open-Settings-ByButton
            $m = Trace-Mark; Recorder-Start
            Send-ActivationChord -AllowedForeground $noto.Hwnd
            $ignored = (Wait-Trace $m 'Noto\.Hotkey pressed ignored=recording') -and (Ev).WsVisible -and (Fg) -eq $noto.Hwnd -and (Recorder-Status) -match '^Recording'
            Add-Result $run '14 recording suspends the current chord: a press is ignored, Settings stays' ($ignored -and (Trace-Since $m) -match 'Noto\.Hotkey suspended=True') "status='$(Recorder-Status)'; trace: $(Trace-Since $m)"

            Send-ToNoto @($VK.CTRL, $VK.ALT, $VK.LWIN) $VkNew
            $rebound = Wait-Trace $m 'Noto\.Hotkey rebind outcome=Rebound .*to=Ctrl\+Alt\+Win\+F9 registered=True'
            Add-Result $run '14 a new chord registers, the old is released, the new is stored and shown' ($rebound -and (Stored 'activation.hotkey.binding') -eq 'Ctrl+Alt+Win+F9' -and -not (Chord-Free $VkNew) -and (Chord-Free $VkSpace) -and (Recorder-Status) -match 'now Ctrl\+Alt\+Win\+F9' -and (Trace-Since $m) -match 'suspended=False') "status='$(Recorder-Status)'; trace: $(Trace-Since $m)"
            Add-Result $run '14 the new chord shows Noto' (Shown-By $VkNew '14 new') ''

            $holder = Start-ChordHolder $VkHeld
            try {
                $m = Trace-Mark; Recorder-Start
                Send-ToNoto @($VK.CTRL, $VK.ALT, $VK.LWIN) $VkHeld
                $refused = Wait-Trace $m 'Noto\.Hotkey rebind outcome=Refused refusal=Conflict .*registered=True'
                Add-Result $run '14 a chord another process holds is refused and announced; nothing changes' ($refused -and (Recorder-Status) -match 'in use by another app' -and (Stored 'activation.hotkey.binding') -eq 'Ctrl+Alt+Win+F9' -and -not (Chord-Free $VkNew)) "status='$(Recorder-Status)'; trace: $(Trace-Since $m)"
                Add-Result $run '14 after the refusal the current chord still works' (Shown-By $VkNew '14 after refusal') ''
            }
            finally { $null = Stop-HotkeyHolder $holder }

            Noto-Front '14 cancel'
            $m = Trace-Mark; Recorder-Start
            Send-ToNoto @() 0x1B
            Add-Result $run '14 Escape cancels and gives the chord back' ((Recorder-Status) -match '^Cancelled' -and (Wait-Trace $m 'suspended=False') -and (Shown-By $VkNew '14 after Escape')) "trace: $(Trace-Since $m)"

            Noto-Front '14 focus'
            $m = Trace-Mark; Recorder-Start
            $null = Set-ForegroundByKeyboard $target
            Add-Result $run '14 losing activation cancels and gives the chord back' ((Wait-Trace $m 'suspended=False') -and (Shown-By $VkNew '14 after focus loss')) "trace: $(Trace-Since $m)"

            Restart-Noto
            Add-Result $run '14 a restart keeps the new binding' ((Stored 'activation.hotkey.binding') -eq 'Ctrl+Alt+Win+F9' -and -not (Chord-Free $VkNew) -and (Shown-By $VkNew '14 after restart')) ''

            $m = Trace-Mark; Recorder-Start
            Send-ToNoto @($VK.CTRL, $VK.ALT, $VK.LWIN) $VkSpace
            Add-Result $run '14 the default is restored through the recorder' ((Wait-Trace $m 'outcome=Rebound .*to=Ctrl\+Alt\+Win\+SPACE registered=True') -and -not (Chord-Free $VkSpace) -and (Chord-Free $VkNew)) "trace: $(Trace-Since $m)"
        }

        # ------------------------------------------------ 13 integrity
        $keys = @((Get-NotoSettings $run -Exe $NotoExe -DataRoot $data | ConvertFrom-Json -AsHashtable).Keys | Where-Object { $_ -notlike 'workspace.width::*' })
        Add-Result $run '13 the isolated database holds only declared setting keys' (@($keys | Where-Object { $_ -notin $Declared }).Count -eq 0) "keys=$($keys -join ',')"
        Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $script:noto) ''
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
}
exit $code
