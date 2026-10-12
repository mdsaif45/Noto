#Requires -Version 7.4
<#
.SYNOPSIS
  #16 slice 7: the notification-area icon and the application's lifetime (ADR-014).

.DESCRIPTION
  One isolated data root seeded with a folder and a note. Noto and the target app are started by the shell. On
  Windows 11 a new icon sits in the overflow ("Show hidden icons"): the harness opens it with a real click on the
  taskbar's chevron and finds Noto's icon by UI Automation, changing no setting. Clicks land only where the root
  window under the point is the intended shell surface; keys go only to the process that owns the foreground (the
  shell, or Noto's own menu). Every claim that Noto took the foreground is bracketed by negative controls; any grant
  makes the run INVALID. Noto's Noto.Tray, Noto.Session, Noto.Workspace and Noto.Instance diagnostics are read from
  the debug-output channel. The menu's rows are read from the menu itself (MN_GETHMENU, GetMenuState).

    1  launch -> exactly one icon (by its window and id, and in the overflow), one visible Noto window, one process
    2  a click on the icon while hidden -> shown, docked, in front, focused (a launch)
    3  a click while minimized -> restored, docked, in front
    4  Enter on the icon while shown behind another app (pinned) -> brought forward; Space likewise
    5  opening the overflow over a shown, unpinned Noto takes activation and hides it (measured adaptation); the
       click then shows it again: the icon never hides Noto
    6  a double click while hidden -> shown once; a second selection, if Windows delivers one, finds it in front
       and does nothing
    7  the menu's rows in each state: hidden, minimized, behind; pinned is checked; by right click and by Shift+F10
    8  the menu dismissed by Escape and by a click on another app -> nothing changes; no Noto window left in front
    9  menu Show while hidden, minimized and behind; menu Hide while behind, with unsaved text saved first, and with a
       failed save kept shown with its notice
    10 menu Keep Noto open: the header follows, losing activation no longer hides, a second choice unpins, and a
       relaunch starts unpinned
    11 menu Quit: clean exit with the icon, mutex and pipe gone; unsaved text saved; a failed save keeps Noto open and
       shows it (restored from minimized) with its notice; Quit again discards and exits
    12 TaskbarCreated sent to the icon's window -> re-added once, nothing else changes
    13 the session's end through Restart Manager: clean -> exits; unsaved text -> saved, exits; a failed save ->
       refused, Noto stays with its text; exactly one shutdown and one release throughout
    14 single instance: a second launch while hidden shows Noto, and there is still one icon; after Quit a relaunch
       has exactly one icon
    15 startup: the icon clicked the moment it appears never hides Noto
    16 a killed Noto: what remains in the overflow is recorded (measured, not asserted)
    17 (-ExplorerRestart, run last) a real Explorer restart -> the icon comes back once; Noto's pid, presence and pin
       are unchanged; no workspace request is made
  Throughout: while the workspace is hidden, no Noto window may be the foreground window or hold keyboard focus.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Tray.ps1 -NotoExe <path>\Noto.exe [-ExplorerRestart]
#>
param([Parameter(Mandatory)] [string] $NotoExe, [switch] $ExplorerRestart, [string[]] $Only = @())
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path

if (-not ('NotoVal.Tray' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text; using System.Threading.Tasks;
namespace NotoVal {
public static class Tray {
    [StructLayout(LayoutKind.Sequential)] struct NII { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guid; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NII id, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr result);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr m, uint id, uint flags);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    /// Whether the shell knows an icon for this window and id.
    public static bool IconExists(IntPtr hwnd) { var n = new NII(); n.cbSize = (uint)Marshal.SizeOf(n); n.hWnd = hwnd; n.uID = 1; RECT r; return Shell_NotifyIconGetRect(ref n, out r) == 0; }
    /// The open menu's four rows: "Show=E Hide=D KeepOpen=EC Quit=E" (E enabled, D disabled, C checked, - absent).
    public static string MenuRows(IntPtr menuWindow) {
        IntPtr h = SendMessage(menuWindow, 0x01E1, IntPtr.Zero, IntPtr.Zero);   // MN_GETHMENU
        if (h == IntPtr.Zero) return "no-menu";
        string[] names = { "Show", "Hide", "KeepOpen", "Quit" }; var sb = new StringBuilder();
        for (uint id = 1; id <= 4; id++) {
            uint s = GetMenuState(h, id, 0);
            string v = s == 0xFFFFFFFF ? "-" : (((s & 3) != 0) ? "D" : "E") + (((s & 8) != 0) ? "C" : "");
            sb.Append(names[id - 1]).Append('=').Append(v).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }
    [StructLayout(LayoutKind.Sequential)] struct FT { public uint lo, hi; }
    [StructLayout(LayoutKind.Sequential)] struct RMP { public int pid; public FT start; }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint h, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint h, uint nf, string[] f, uint na, RMP[] a, uint ns, string[] s);
    [DllImport("rstrtmgr.dll")] static extern int RmShutdown(uint h, uint flags, IntPtr cb);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint h);
    /// Restart Manager, not forced: the session-end messages a real installer sends. Returns RmShutdown's code, or -1 on a timeout.
    public static int RmClose(int pid, long startFileTime, int timeoutMs) {
        uint h; var key = new StringBuilder(64);
        if (RmStartSession(out h, 0, key) != 0) return -2;
        var p = new RMP { pid = pid, start = new FT { lo = (uint)(startFileTime & 0xFFFFFFFF), hi = (uint)(startFileTime >> 32) } };
        if (RmRegisterResources(h, 0, null, 1, new[] { p }, 0, null) != 0) { RmEndSession(h); return -3; }
        var t = Task.Run(() => RmShutdown(h, 0, IntPtr.Zero));
        if (!t.Wait(timeoutMs)) return -1;
        RmEndSession(h); return t.Result;
    }
}
}
'@
}

$run = New-ValidationRun 'tray'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F007'
$NoteId = '01J0000000000000000000N007'
$Original = 'original note text'
$TaskbarCreated = [NotoVal.Tray]::RegisterWindowMessage('TaskbarCreated')

# ---------------------------------------------------------------- helpers

function Want([string] $Id) { $ids = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ }); $ids.Count -eq 0 -or $ids -contains $Id }
function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function FgPid { $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId((Fg), [ref]$p); [int]$p }
function Ev { Get-WindowEvidence $noto }
function Show-State($e) { "visible=$($e.WsVisible) minimized=$($e.Minimized) fg=$($e.IsForeground)(pid $($e.ForegroundPid)) focus=$($e.KeyboardFocus)" }
function Is-Front($e) { $e.WsVisible -and -not $e.Minimized -and $e.Cloaked -eq 0 -and $e.IsForeground -and $e.KeyboardFocus -and (Test-DockedAt $e 'Right') }
function Is-Hidden($e) { -not $e.WsVisible }
function Wait-For([scriptblock] $Condition, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = Ev; if (& $Condition $e) { return $e }; Start-Sleep -Milliseconds 150 }
    Ev
}
function Trace-Mark { @($log.Lines($noto.Pid)).Count }
function Trace-Since([int] $Mark) { @($log.Lines($noto.Pid) | Select-Object -Skip $Mark) -join ' | ' }
function Trace-Of([int] $ProcessId) { @($log.Lines($ProcessId)) -join ' | ' }

function Never-Hidden-Active([string] $Label) {
    <# While the workspace is hidden, no window of Noto's - the workspace or the icon's hidden window - is in front or focused. #>
    $e = Ev
    $bad = (Is-Hidden $e) -and ($e.ForegroundPid -eq $noto.Pid -or $e.FocusRootPid -eq $noto.Pid)
    Add-Result $run "$Label -> no Noto window active while hidden" (-not $bad) "$(Show-State $e)"
}

function Root-Class([int] $X, [int] $Y) { [NotoVal.Instance]::RootClassAt($X, $Y) }
function Describe-At([int] $X, [int] $Y) {
    <# The top-level window at a screen point - class, title and owning process - for an INVALID run to name what was there. #>
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $X; $pt.Y = $Y
    $root = [NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2)
    $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId($root, [ref]$p)
    "class '$(Root-Class $X $Y)' title '$([NotoVal.Win32]::Text($root))' process $((Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName) ($p)"
}
function Click-Class([int] $X, [int] $Y, [string[]] $Classes, [switch] $Right, [switch] $Double) {
    if ($Classes -notcontains (Root-Class $X $Y)) { throw "refusing to click at ($X,$Y): expected $($Classes -join ' or '), found $(Describe-At $X $Y)" }
    [void][NotoVal.Win32]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 200
    if ($Classes -notcontains (Root-Class $X $Y)) { throw "the window under the pointer at ($X,$Y) changed to $(Describe-At $X $Y)" }
    if ($Right) { [NotoVal.Tray]::mouse_event(8, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Tray]::mouse_event(16, 0, 0, 0, [IntPtr]::Zero) }
    elseif ($Double) { 1..2 | ForEach-Object { [NotoVal.Tray]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Tray]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60 } }
    else { [NotoVal.Tray]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Tray]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 1200
}
function Centre($Rect) { @([int]($Rect.X + $Rect.Width / 2), [int]($Rect.Y + $Rect.Height / 2)) }

function Tray-Buttons([string] $HostClass) {
    $h = [NotoVal.Win32]::FindWindow($HostClass, [NullString]::Value)
    if ($h -eq [IntPtr]::Zero) { return @() }
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    @($AE::FromHandle($h).FindAll($Scope::Descendants, $cond))
}
function Chevron {
    <# The taskbar's overflow button; its name reads Show or Hide Hidden Icons depending on the overflow. #>
    Tray-Buttons 'Shell_TrayWnd' | Where-Object { $_.Current.Name -match 'Hidden Icons' -and $_.Current.BoundingRectangle.Width -gt 0 } | Select-Object -First 1
}
function Flyout { $h = [NotoVal.Win32]::FindWindow('TopLevelWindowForOverflowXamlIsland', [NullString]::Value); if ($h -ne [IntPtr]::Zero -and [NotoVal.Win32]::IsWindowVisible($h)) { $h } else { [IntPtr]::Zero } }
function Open-Overflow {
    if ((Flyout) -ne [IntPtr]::Zero) { return }
    $chevron = Chevron
    if (-not $chevron) { throw 'the taskbar has no "Show Hidden Icons" button' }
    $xy = Centre $chevron.Current.BoundingRectangle; Click-Class $xy[0] $xy[1] @('Shell_TrayWnd')
    $deadline = (Get-Date).AddSeconds(3); while ((Get-Date) -lt $deadline -and (Flyout) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    if ((Flyout) -eq [IntPtr]::Zero) { throw 'the overflow did not open' }
}
function Noto-Icons { @(Tray-Buttons 'TopLevelWindowForOverflowXamlIsland' | Where-Object { $_.Current.Name -eq 'Noto' }) + @(Tray-Buttons 'Shell_TrayWnd' | Where-Object { $_.Current.Name -eq 'Noto' }) }
function Noto-Icon { Open-Overflow; $i = Noto-Icons | Select-Object -First 1; if (-not $i) { throw 'no Noto icon in the overflow' }; $i }
function Keys-ToShell([int[]] $Mods, [int] $Key) {
    if ((FgPid) -ne (Get-ShellPid)) { throw 'refusing keys: the shell does not own the foreground' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 1200
}
function Keys-ToNotoMenu([int] $Key) {
    if ((FgPid) -ne $noto.Pid -or (Menu-Window) -eq [IntPtr]::Zero) { throw 'refusing keys: Noto''s menu is not open in front' }
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true))); Start-Sleep -Milliseconds 1200
}
function Close-Flyout {
    <# Escape when the shell is in front; otherwise a second click on the chevron, which closes the overflow. #>
    if ((Flyout) -eq [IntPtr]::Zero) { return }
    if ((FgPid) -eq (Get-ShellPid)) { Keys-ToShell @() 0x1B }
    if ((Flyout) -ne [IntPtr]::Zero) {
        $chevron = Chevron
        $xy = Centre $chevron.Current.BoundingRectangle; Click-Class $xy[0] $xy[1] @('Shell_TrayWnd')
    }
    if ((Flyout) -ne [IntPtr]::Zero) { throw 'the overflow did not close' }
}
function Menu-Window { $h = [NotoVal.Win32]::FindWindow('#32768', [NullString]::Value); if ($h -ne [IntPtr]::Zero -and [NotoVal.Win32]::IsWindowVisible($h)) { $h } else { [IntPtr]::Zero } }

function Click-Icon([switch] $Double, [switch] $Right) {
    $xy = Centre (Noto-Icon).Current.BoundingRectangle
    Click-Class $xy[0] $xy[1] @('TopLevelWindowForOverflowXamlIsland') -Double:$Double -Right:$Right
}
function Key-Icon([int[]] $Mods, [int] $Key) {
    <# Focus the icon by UI Automation (no input), then a real key to the shell. Works where Noto covers the overflow. #>
    $i = Noto-Icon; $i.SetFocus(); Start-Sleep -Milliseconds 400
    if ($AE::FocusedElement.Current.Name -ne 'Noto') { throw 'the Noto icon did not take focus' }
    Keys-ToShell $Mods $Key
}
function Open-Menu([switch] $Mouse) {
    if ($Mouse) { Click-Icon -Right } else { Key-Icon @(0x10) 0x79 }
    $deadline = (Get-Date).AddSeconds(3); while ((Get-Date) -lt $deadline -and (Menu-Window) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    if ((Menu-Window) -eq [IntPtr]::Zero) { throw 'the menu did not open' }
    [NotoVal.Tray]::MenuRows((Menu-Window))
}
function Choose([string] $Letter) { Keys-ToNotoMenu ([int][char]$Letter.ToUpperInvariant()) }   # the rows' access keys: S, H, K, Q

function Ctrl([string] $Label, [IntPtr] $Away) {
    $neg = Test-NegativeControl $run $Away
    $run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $run "$Label negative control was granted the foreground: $($neg.Detail)" }
}
function Target-Front {
    <# As Set-ForegroundByKeyboard, but a refusal names the window that covers the target, so an INVALID run says why. #>
    $r = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($target.Hwnd, [ref]$r)
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = [int](($r.Left + $r.Right) / 2); $pt.Y = [int]($r.Top + 80)
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $target.Hwnd) {
        throw "the target is covered at ($($pt.X),$($pt.Y)) by $(Describe-At $pt.X $pt.Y)"
    }
    $null = Set-ForegroundByKeyboard $target
}
function Noto-Front {
    if ((Fg) -eq $noto.Hwnd) { return }
    Target-Front; Send-ActivationChord -AllowedForeground $target.Hwnd
    $null = Wait-For { param($e) $e.IsForeground }
    if ((Fg) -ne $noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}
function Hide-Noto { Noto-Front; Send-ActivationChord -AllowedForeground $noto.Hwnd; if (-not (Is-Hidden (Wait-For { param($e) -not $e.WsVisible }))) { throw 'the hotkey did not hide Noto' } }

function Find-ById([string] $Id, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, $cond); if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Visible-Id([string] $Id) { $e = $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)); [bool]($e -and -not $e.Current.IsOffscreen) }
function First-Item([string] $ListId) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $list = Find-ById $ListId; $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) { $i = $list.FindFirst($Scope::Descendants, $cond); if ($i) { return $i }; Start-Sleep -Milliseconds 200 }
    throw "list '$ListId' has no item"
}
function Select-AndFocus($Item) { $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $Item.SetFocus(); Start-Sleep -Milliseconds 300 }
function Send-ToNoto([int[]] $Mods, [int] $Key) {
    if ((Fg) -ne $noto.Hwnd) { throw 'refusing keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 700
}
function Open-Editor {
    Noto-Front
    if (Visible-Id 'NoteEditor') { return }
    Select-AndFocus (First-Item 'FolderList'); Send-ToNoto @(0x11) 0x28
    Select-AndFocus (First-Item 'NoteList'); Send-ToNoto @() 0x0D
    $null = Find-ById 'NoteEditor'
}
function Set-Editor([string] $Text) { (Find-ById 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text); Start-Sleep -Milliseconds 300 }
function Notice { try { (Find-ById 'EditorNoticeText' 2).Current.Name } catch { '' } }
function Pin-State { (Find-ById 'PinToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() }
function Row([string] $Column) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql "SELECT $Column FROM Notes WHERE Id = `$n" -Parameters @{ '$n' = $NoteId } }
function Bin-Note { $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = `$t WHERE Id = `$n" -Parameters @{ '$t' = [DateTimeOffset]::UtcNow.ToString('O'); '$n' = $NoteId } }
function Restore-Note { $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "UPDATE Notes SET DeletedAt = NULL, Content = `$c WHERE Id = `$n" -Parameters @{ '$c' = $Original; '$n' = $NoteId } }

function Tray-Window($Noto) { [NotoVal.Win32]::TopWindows([uint32]$Noto.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto tray' } | Select-Object -First 1 }
function Icon-Count { Open-Overflow; $n = @(Noto-Icons).Count; Close-Flyout; $n }
function Exited($Noto, [int] $Seconds = 10) { $p = Get-Process -Id $Noto.Pid -ErrorAction SilentlyContinue; (-not $p) -or $p.WaitForExit($Seconds * 1000) }
function Start-Fresh([hashtable] $Settings = @{}) {
    if ($script:noto -and (Get-Process -Id $script:noto.Pid -ErrorAction SilentlyContinue)) { if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly before a restart' } }
    Close-Flyout
    $all = @{ 'workspace.hide-on-deactivation' = 'True' }; foreach ($k in $Settings.Keys) { $all[$k] = $Settings[$k] }
    foreach ($k in $all.Keys) { Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key $k -Value $all[$k] }
    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
    $script:noto | Add-Member -NotePropertyName Tray -NotePropertyValue (Tray-Window $script:noto) -Force
    $script:noto | Add-Member -NotePropertyName Start -NotePropertyValue ((Get-Process -Id $script:noto.Pid).StartTime.ToFileTime()) -Force
    if (-not $script:noto.Tray) { throw 'Noto has no tray window' }
}

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title '#16 slice 7: the tray and the application lifetime' -Body {
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $script:data = Join-Path $run.Root 'root'; New-Item -ItemType Directory $data | Out-Null
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
        if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Validation', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
        $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" -Parameters @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = $Original; '$t' = $now }
        $script:target = Start-TargetApp $run
        $pipeName = [NotoVal.Instance]::PipeName($data)

        Start-Fresh
        if (Want 1) {
            $windows = @([NotoVal.Win32]::TopWindows([uint32]$noto.Pid) | Where-Object { [NotoVal.Win32]::IsWindowVisible($_) })
            $count = Icon-Count
            Add-Result $run '1  one icon: known to the shell by its window and id' ([NotoVal.Tray]::IconExists($noto.Tray)) "trace: $(Trace-Of $noto.Pid)"
            Add-Result $run '1  one icon: exactly one "Noto" in the overflow and taskbar' ($count -eq 1) "count=$count"
            Add-Result $run '1  one visible Noto window, one Noto process; the icon window is hidden' ($windows.Count -eq 1 -and @(Get-Process -Name Noto).Count -eq 1 -and -not [NotoVal.Win32]::IsWindowVisible($noto.Tray)) "visible=$($windows.Count) processes=$(@(Get-Process -Name Noto).Count)"
            Add-Result $run '1  the trace reports the icon added' ((Trace-Of $noto.Pid) -match 'Noto\.Tray added=True') ''
        }

        if (Want 2) {
            Close-Flyout
            Hide-Noto; Target-Front
            Ctrl '2 before' $noto.Hwnd
            $m = Trace-Mark; Click-Icon
            $e = Wait-For { param($e) $e.IsForeground }
            Add-Result $run '2  click while hidden -> shown, docked, in front, focused' (Is-Front $e) "$(Show-State $e); trace: $(Trace-Since $m)"
            Add-Result $run '2  ... as a launch' ((Trace-Since $m) -match 'request=Launch action=Show' -and (Trace-Since $m) -match 'Noto\.Tray invoked action=Show') ''
            Ctrl '2 after' $target.Hwnd
        }

        if (Want 3) {
            Close-Flyout
            Noto-Front; $null = Invoke-Minimize $noto; Target-Front
            Ctrl '3 before' $noto.Hwnd
            $m = Trace-Mark; Click-Icon
            $e = Wait-For { param($e) $e.IsForeground }
            Add-Result $run '3  click while minimized -> restored, docked, in front' (Is-Front $e) "$(Show-State $e); trace: $(Trace-Since $m)"
            Add-Result $run '3  ... as a launch' ((Trace-Since $m) -match 'request=Launch action=Restore') ''
            Ctrl '3 after' $target.Hwnd
        }

        if (Want 4) {
            Close-Flyout
            foreach ($key in @(@{ N = 'Enter'; K = 0x0D }, @{ N = 'Space'; K = 0x20 })) {
                Noto-Front
                if ((Pin-State) -ne 'On') { (Find-ById 'PinToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300 }
                Target-Front
                $e = Ev; if (-not $e.WsVisible -or $e.IsForeground) { throw 'Noto is not shown behind the target' }
                Ctrl "4 $($key.N) before" $noto.Hwnd
                $m = Trace-Mark; Key-Icon @() $key.K
                $e = Wait-For { param($e) $e.IsForeground }
                Add-Result $run "4  $($key.N) on the icon while shown behind (pinned) -> brought forward" (Is-Front $e) "$(Show-State $e); trace: $(Trace-Since $m)"
                Add-Result $run "4  ... as a launch (focus)" ((Trace-Since $m) -match 'request=Launch action=Focus') ''
                Ctrl "4 $($key.N) after" $target.Hwnd
            }
            (Find-ById 'PinToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300
            if ((Pin-State) -ne 'Off') { throw 'could not unpin' }
        }

        if (Want 5) {
            Close-Flyout
            Noto-Front
            $m = Trace-Mark; Open-Overflow
            $hidden = Wait-For { param($e) -not $e.WsVisible }
            Add-Result $run '5  opening the overflow over a shown, unpinned Noto takes activation and hides it (measured)' (Is-Hidden $hidden) "$(Show-State $hidden); trace: $(Trace-Since $m)"
            Never-Hidden-Active '5  overflow open'
            Click-Icon
            $e = Wait-For { param($e) $e.IsForeground }
            Add-Result $run '5  the click then shows it: the icon never hides Noto' ((Is-Front $e) -and (Trace-Since $m) -notmatch 'Noto\.Tray invoked action=Hide') "$(Show-State $e); trace: $(Trace-Since $m)"
        }

        if (Want 6) {
            Close-Flyout
            Hide-Noto; Target-Front
            $m = Trace-Mark; Click-Icon -Double
            $e = Wait-For { param($e) $e.IsForeground }
            $t = Trace-Since $m
            $invoked = @([regex]::Matches($t, 'Noto\.Tray invoked action=(\w+)') | ForEach-Object { $_.Groups[1].Value })
            Add-Result $run '6  double click while hidden -> shown once, in front' (Is-Front $e) "$(Show-State $e); invoked=$($invoked -join ',')"
            # Windows delivers a second selection only if the second click still reaches the icon: the first shows Noto,
            # which is topmost and can cover the overflow. Either way, nothing after the first may act.
            Add-Result $run '6  the first selection shows; any further one does nothing' ($invoked.Count -ge 1 -and $invoked[0] -eq 'Show' -and @($invoked | Select-Object -Skip 1 | Where-Object { $_ -ne 'None' }).Count -eq 0) "invoked=$($invoked -join ','); trace: $t"
        }

        if (Want 7) {
            Close-Flyout
            Hide-Noto; Target-Front
            $rows = Open-Menu -Mouse; Keys-ToNotoMenu 0x1B
            Add-Result $run '7  menu, hidden (right click): Show, Keep open, Quit; not Hide' ($rows -eq 'Show=E Hide=D KeepOpen=E Quit=E') $rows
            Close-Flyout
            Noto-Front; $null = Invoke-Minimize $noto; Target-Front
            $rows = Open-Menu; Keys-ToNotoMenu 0x1B
            Add-Result $run '7  menu, minimized (Shift+F10): Show, Keep open, Quit; not Hide' ($rows -eq 'Show=E Hide=D KeepOpen=E Quit=E') $rows
            Close-Flyout
            Noto-Front
            (Find-ById 'PinToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300
            Target-Front
            $rows = Open-Menu; Keys-ToNotoMenu 0x1B
            Add-Result $run '7  menu, behind and pinned: all four; Keep open checked' ($rows -eq 'Show=E Hide=E KeepOpen=EC Quit=E') $rows
            Close-Flyout
            Noto-Front; (Find-ById 'PinToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300
        }

        if (Want 8) {
            Close-Flyout
            Hide-Noto; Target-Front
            $m = Trace-Mark
            $rows = try { Open-Menu } catch { $null }
            # The menu's window takes the foreground: an activation that must not land on the hidden workspace.
            Add-Result $run '8  the menu over a hidden Noto opens and leaves it hidden' ([bool]$rows -and (Is-Hidden (Ev))) "rows=$rows $(Show-State (Ev)); trace: $(Trace-Since $m)"
            if ((Menu-Window) -ne [IntPtr]::Zero -and (FgPid) -eq $noto.Pid) { Keys-ToNotoMenu 0x1B }
            Add-Result $run '8  menu dismissed by Escape while hidden -> still hidden; the icon window is not left in front' ((Is-Hidden (Ev)) -and (Fg) -ne $noto.Tray) "fg pid $(FgPid); trace: $(Trace-Since $m)"
            Never-Hidden-Active '8  after Escape'
            Add-Result $run '8  ... the menu''s activation did not land and was not reconciled' ((Trace-Since $m) -match 'request=Activated action=None[^|]*landed=False' -and (Trace-Since $m) -notmatch 'action=Show') ''
            Close-Flyout
            Target-Front
            Hide-Noto; Target-Front
            $m = Trace-Mark; $rows = try { Open-Menu -Mouse } catch { $null }
            Add-Result $run '8  the menu over a hidden Noto opens by right click, Noto still hidden' ([bool]$rows -and (Is-Hidden (Ev))) "rows=$rows $(Show-State (Ev))"
            if (-not $rows) { if ((Menu-Window) -ne [IntPtr]::Zero -and (FgPid) -eq $noto.Pid) { Keys-ToNotoMenu 0x1B } }
            else {
            $r = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($target.Hwnd, [ref]$r)
            $tx = [int](($r.Left + $r.Right) / 2); $ty = [int]($r.Top + 80)
            if ((Root-Class $tx $ty) -notlike 'WindowsForms*') { throw "the target is not under the click-away point ($tx,$ty): found $(Describe-At $tx $ty)" }
            [void][NotoVal.Win32]::SetCursorPos($tx, $ty); Start-Sleep -Milliseconds 200
            [NotoVal.Tray]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Tray]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
            Add-Result $run '8  menu dismissed by a click on another app -> closed, that app in front, Noto still hidden' ((Menu-Window) -eq [IntPtr]::Zero -and (Fg) -eq $target.Hwnd -and (Is-Hidden (Ev))) "fg pid $(FgPid); trace: $(Trace-Since $m)"
            Never-Hidden-Active '8  after click-away'
            }
        }

        if (Want 9) {
            Close-Flyout
            Hide-Noto; Target-Front
            Ctrl '9 show-hidden before' $noto.Hwnd
            $m = Trace-Mark; $null = Open-Menu; Choose 'S'
            $e = Wait-For { param($e) $e.IsForeground }
            Add-Result $run '9  menu Show while hidden -> shown, in front (a launch)' ((Is-Front $e) -and (Trace-Since $m) -match 'Noto\.Tray command=Show' -and (Trace-Since $m) -match 'request=Launch action=Show') "$(Show-State $e); trace: $(Trace-Since $m)"
            Ctrl '9 show-hidden after' $target.Hwnd

            $null = Invoke-Minimize $noto; Target-Front
            $m = Trace-Mark; $null = Open-Menu; Choose 'S'
            $e = Wait-For { param($e) $e.IsForeground }
            Add-Result $run '9  menu Show while minimized -> restored, in front' ((Is-Front $e) -and (Trace-Since $m) -match 'request=Launch action=Restore') "$(Show-State $e); trace: $(Trace-Since $m)"

            Start-Fresh @{ 'workspace.hide-on-deactivation' = 'False' }
            Open-Editor
            $text = "hidden from the tray $([guid]::NewGuid().ToString('N').Substring(0, 6))"; Set-Editor $text
            Target-Front
            $m = Trace-Mark; $null = Open-Menu; Choose 'H'
            $e = Wait-For { param($e) -not $e.WsVisible }
            Add-Result $run '9  menu Hide while behind with unsaved text -> saved, then hidden (a dismissal)' ((Is-Hidden $e) -and (Row 'Content') -eq $text -and (Trace-Since $m) -match 'request=Dismiss action=Hide') "content saved=$((Row 'Content') -eq $text); trace: $(Trace-Since $m)"
            Never-Hidden-Active '9  after menu Hide'

            Noto-Front; Set-Editor "cannot be saved $([guid]::NewGuid().ToString('N').Substring(0, 6))"; Bin-Note
            Target-Front
            $m = Trace-Mark; $null = Open-Menu; Choose 'H'
            Start-Sleep -Milliseconds 800
            $e = Ev
            Add-Result $run '9  menu Hide with a failed save -> kept shown, with the notice' ($e.WsVisible -and (Notice) -match 'recycle bin') "$(Show-State $e) notice='$(Notice)'; trace: $(Trace-Since $m)"
            Add-Result $run '9  ... and the icon window is not left in front' ((Fg) -ne $noto.Tray) "fg pid $(FgPid)"
            Restore-Note
            Close-Flyout
            Start-Fresh
        }

        if (Want 10) {
            Close-Flyout
            Noto-Front; Target-Front
            $m = Trace-Mark; $null = Open-Menu; Choose 'K'
            Close-Flyout
            Noto-Front
            Add-Result $run '10 Keep Noto open from the tray -> the header shows it pinned' ((Pin-State) -eq 'On' -and (Trace-Since $m) -match 'Noto\.Workspace pinned=True') "pin=$(Pin-State); trace: $(Trace-Since $m)"
            Target-Front
            $e = Wait-For { param($e) -not $e.WsVisible } 2
            Add-Result $run '10 pinned from the tray, losing activation no longer hides' ($e.WsVisible) "$(Show-State $e)"
            $rows = Open-Menu; Choose 'K'
            Close-Flyout
            Noto-Front
            Add-Result $run '10 a second choice unpins; the menu showed it checked' ((Pin-State) -eq 'Off' -and $rows -match 'KeepOpen=EC') "pin=$(Pin-State) rows=$rows"
            Noto-Front; $null = Open-Menu; Choose 'K'; Close-Flyout
            Start-Fresh
            Noto-Front
            Add-Result $run '10 pinned at exit, a relaunch starts unpinned' ((Pin-State) -eq 'Off') "pin=$(Pin-State)"
        }

        if (Want 11) {
            Close-Flyout
            Target-Front
            $m = Trace-Mark; $old = $noto; $null = Open-Menu; Choose 'Q'
            $gone = Exited $old
            Start-Sleep -Milliseconds 500
            $t = Trace-Of $old.Pid
            Add-Result $run '11 Quit, clean -> exits; icon, mutex and pipe gone' ($gone -and -not [NotoVal.Tray]::IconExists($old.Tray) -and -not [NotoVal.Instance]::MutexExists($pipeName) -and -not [NotoVal.Instance]::PipeExists($pipeName)) "exited=$gone mutex=$([NotoVal.Instance]::MutexExists($pipeName)) pipe=$([NotoVal.Instance]::PipeExists($pipeName))"
            Add-Result $run '11 ... one shutdown, one release' (([regex]::Matches($t, 'shutting-down')).Count -eq 1 -and ([regex]::Matches($t, 'Noto\.Instance released')).Count -eq 1) "trace: $t"
            Close-Flyout

            Start-Fresh; Open-Editor
            $text = "saved by quit $([guid]::NewGuid().ToString('N').Substring(0, 6))"; Set-Editor $text
            Target-Front; $old = $noto; $null = Open-Menu; Choose 'Q'
            Add-Result $run '11 Quit with unsaved text -> saved, exits' ((Exited $old) -and (Row 'Content') -eq $text) "content saved=$((Row 'Content') -eq $text)"
            Close-Flyout

            Start-Fresh @{ 'workspace.hide-on-deactivation' = 'False' }; Open-Editor
            Set-Editor "cannot be saved by quit $([guid]::NewGuid().ToString('N').Substring(0, 6))"; Bin-Note; $kept = Row 'Content'
            $null = Invoke-Minimize $noto; Target-Front
            $m = Trace-Mark; $null = Open-Menu; Choose 'Q'
            $e = Wait-For { param($e) $e.IsForeground }
            Add-Result $run '11 Quit with a failed save -> stays open, restored and in front with its notice' ((Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue) -and (Is-Front $e) -and (Notice) -match 'Close the window again') "$(Show-State $e) notice='$(Notice)'; trace: $(Trace-Since $m)"
            Add-Result $run '11 ... no shutdown began' ((Trace-Since $m) -notmatch 'shutting-down' -and (Trace-Since $m) -match 'Noto\.Tray quit-cancelled action=Restore') ''
            Close-Flyout
            Target-Front; $old = $noto; $null = Open-Menu; Choose 'Q'
            Add-Result $run '11 Quit again with the same text -> discards and exits; the row unchanged' ((Exited $old) -and (Row 'Content') -eq $kept) "content='$(Row 'Content')' kept='$kept'"
            Restore-Note; Close-Flyout
            Start-Fresh
        }

        if (Want 12) {
            Close-Flyout
            Hide-Noto; Target-Front
            $m = Trace-Mark; $pid0 = $noto.Pid
            $res = [IntPtr]::Zero; [void][NotoVal.Tray]::SendMessageTimeout($noto.Tray, $TaskbarCreated, [IntPtr]::Zero, [IntPtr]::Zero, 2, 5000, [ref]$res)
            Start-Sleep -Milliseconds 800
            $t = Trace-Since $m
            Add-Result $run '12 TaskbarCreated -> re-added once; still exactly one icon' ($t -match 'Noto\.Tray readded added=True' -and (Icon-Count) -eq 1 -and [NotoVal.Tray]::IconExists($noto.Tray)) "trace: $t"
            Add-Result $run '12 ... nothing else changed: same process, still hidden, no request' ((Get-Process -Id $pid0 -ErrorAction SilentlyContinue) -and (Is-Hidden (Ev)) -and $t -notmatch 'request=') "$(Show-State (Ev))"
            Noto-Front
        }

        if (Want 13) {
            Close-Flyout
            Start-Fresh
            $old = $noto; $rm = [NotoVal.Tray]::RmClose($old.Pid, $old.Start, 60000)
            Start-Sleep -Milliseconds 500; $t = Trace-Of $old.Pid
            Add-Result $run '13 session end (Restart Manager), clean -> allowed; exits' ($rm -eq 0 -and (Exited $old)) "RmShutdown=$rm; trace: $t"
            Add-Result $run '13 ... through the one close: one shutdown, one release, despite two close requests' ($t -match 'Noto\.Session query critical=False saved=True' -and $t -match 'Noto\.Session ended ending=True' -and ([regex]::Matches($t, 'shutting-down')).Count -eq 1 -and ([regex]::Matches($t, 'Noto\.Instance released')).Count -eq 1) ''

            Start-Fresh; Open-Editor
            $text = "saved at session end $([guid]::NewGuid().ToString('N').Substring(0, 6))"; Set-Editor $text
            $old = $noto; $rm = [NotoVal.Tray]::RmClose($old.Pid, $old.Start, 60000)
            Add-Result $run '13 session end with unsaved text -> saved before allowing; exits' ($rm -eq 0 -and (Exited $old) -and (Row 'Content') -eq $text) "RmShutdown=$rm content saved=$((Row 'Content') -eq $text); trace: $(Trace-Of $old.Pid)"

            Start-Fresh; Open-Editor
            $text = "cannot be saved at session end $([guid]::NewGuid().ToString('N').Substring(0, 6))"; Set-Editor $text; Bin-Note
            $m = Trace-Mark; $rm = [NotoVal.Tray]::RmClose($noto.Pid, $noto.Start, 60000)
            Start-Sleep -Milliseconds 800
            Add-Result $run '13 session end with a failed save -> refused; Noto stays, text kept' ($rm -ne 0 -and (Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue) -and (Find-ById 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $text) "RmShutdown=$rm; trace: $(Trace-Since $m)"
            Add-Result $run '13 ... no shutdown began' ((Trace-Since $m) -match 'Noto\.Session query critical=False saved=False' -and (Trace-Since $m) -notmatch 'shutting-down') ''
            Restore-Note
            Start-Fresh
        }

        if (Want 14) {
            Close-Flyout
            Hide-Noto; Target-Front
            $m = Trace-Mark
            $view = [NotoVal.ShellLaunch]::DesktopViewDispatch(); $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`"", $run.Root, 'open', 1)
            $e = Wait-For { param($e) $e.WsVisible } 10
            Start-Sleep -Seconds 1
            Add-Result $run '14 a second launch while hidden -> shown; one Noto process; still one icon' ($e.WsVisible -and @(Get-Process -Name Noto).Count -eq 1 -and (Icon-Count) -eq 1) "processes=$(@(Get-Process -Name Noto).Count); trace: $(Trace-Since $m)"
            $old = $noto; Target-Front; $null = Open-Menu; Choose 'Q'; $null = Exited $old; Close-Flyout
            Start-Fresh
            Add-Result $run '14 Quit then relaunch -> exactly one icon' ((Icon-Count) -eq 1) ''
        }

        if (Want 15) {
            Close-Flyout
            if (-not (Stop-Noto $noto)) { throw 'Noto did not exit' }
            $p = Start-ViaShell $run -File $NotoExe -Arguments "--data-root=`"$data`"" -Marker "--data-root=`"$data`""
            $deadline = (Get-Date).AddSeconds(30); $trayHwnd = $null
            while ((Get-Date) -lt $deadline -and -not $trayHwnd) { $trayHwnd = [NotoVal.Win32]::TopWindows([uint32]$p.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto tray' } | Select-Object -First 1; Start-Sleep -Milliseconds 50 }
            $main = [NotoVal.Win32]::TopWindows([uint32]$p.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1
            $script:noto = [pscustomobject]@{ Pid = $p.Pid; Hwnd = $main; Tray = $trayHwnd }
            Click-Icon
            Start-Sleep -Seconds 2
            $e = Ev
            Add-Result $run '15 the icon clicked as soon as it appears -> Noto shown, never hidden by it' ($e.WsVisible -and (Trace-Of $p.Pid) -notmatch 'Noto\.Tray invoked action=Hide') "$(Show-State $e); trace: $(Trace-Of $p.Pid)"
            $script:noto | Add-Member -NotePropertyName Start -NotePropertyValue ((Get-Process -Id $p.Pid).StartTime.ToFileTime()) -Force
            $script:noto | Add-Member -NotePropertyName DataRoot -NotePropertyValue $data -Force
        }

        if (Want 16) {
            Close-Flyout
            Stop-Process -Id $noto.Pid -Force; Start-Sleep -Seconds 1
            Start-Fresh
            Add-Result $run '16 killed, relaunched: icons named Noto in the overflow (measured, not asserted)' $true "count=$(Icon-Count)"
        }

        if ($ExplorerRestart) {
            Close-Flyout
            Hide-Noto; Target-Front
            $m = Trace-Mark; $pid0 = $noto.Pid; $pin0 = 'Off'
            $shell0 = Get-ShellPid
            Stop-Process -Id $shell0 -Force
            $deadline = (Get-Date).AddSeconds(60)
            while ((Get-Date) -lt $deadline -and ((Get-ShellPid) -in 0, $shell0 -or [NotoVal.Win32]::FindWindow('Shell_TrayWnd', [NullString]::Value) -eq [IntPtr]::Zero)) { Start-Sleep -Milliseconds 500 }
            if ((Get-ShellPid) -in 0, $shell0) { throw 'Explorer did not come back within 60 s' }
            $deadline = (Get-Date).AddSeconds(30); while ((Get-Date) -lt $deadline -and (Trace-Since $m) -notmatch 'readded') { Start-Sleep -Milliseconds 500 }
            Start-Sleep -Seconds 5
            $t = Trace-Since $m
            $count = Icon-Count
            Add-Result $run '17 Explorer restarted -> the icon is back, exactly once' ($t -match 'Noto\.Tray readded added=True' -and [NotoVal.Tray]::IconExists($noto.Tray) -and $count -eq 1) "count=$count; trace: $t"
            Add-Result $run '17 ... same Noto process, still hidden, no new instance, no workspace request' ((Get-Process -Id $pid0 -ErrorAction SilentlyContinue) -and @(Get-Process -Name Noto).Count -eq 1 -and (Is-Hidden (Ev)) -and $t -notmatch 'request=(Launch|Toggle|Dismiss)|action=(Show|Hide|Restore|Focus)') "$(Show-State (Ev))"
            Noto-Front
            Add-Result $run '17 ... and still unpinned' ((Pin-State) -eq $pin0) "pin=$(Pin-State)"
        }
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
}
exit $code
