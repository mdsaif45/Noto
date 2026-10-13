#Requires -Version 7.4
<#
.SYNOPSIS
  Run at login (#94, ADR-015): the hidden start, and the tray's Start Noto with Windows.

.DESCRIPTION
  One isolated data root seeded with a folder. Noto, the target app and a bystander are started by the shell, as
  Explorer starts a Run entry at sign-in; a real sign-in cannot be driven from inside a session. From before each
  --login start, every window-show event on the desktop and every foreground change are recorded, so a frame Noto
  presented, or a moment it held the foreground, cannot be missed. Every foreground claim is bracketed by negative
  controls aimed at a background bystander; any grant makes the run INVALID. Clicks land only where the root window
  under the point is the intended one; keys go only to the process that owns the foreground.

    1  three --login starts -> no window of Noto ever shown, never in front, tray icon added, the data root owned
    2  the hotkey after a --login start -> shown, docked, in front, focused, the folder list loaded
    3  minimize, then the hotkey -> restored at the docked rectangle
    4  a tray click after a --login start -> shown, in front (a launch)
    5  a plain second launch after a desktop click -> shown, in front (a launch)
    6  --login while an owner runs, shown and then hidden -> exits, no handoff, the owner unchanged, nothing shown
    7  the tray's Start Noto with Windows row matches Windows' registration, read as the menu opens (read-only)
    8  Quit a --login start never shown -> exits cleanly; icon, mutex and pipe gone
    9  (-RealRegistry, the one approved round trip) choose the row: Run\Noto becomes exactly "<this exe>" --login and
       the row reads checked; choose again: removed. The prior Run and StartupApproved state is snapshotted first and
       restored in finally; StartupApproved must be byte-identical throughout (Noto never writes it).
    0  (-RealRegistry, first) the registry this harness reads and writes is the user's real hive: a scratch value it
       writes must be seen by a process the shell starts, and gone after. A packaged host (an MSIX app such as a
       terminal or an agent) virtualizes HKCU writes for the processes it starts: there, a "restore" would only touch
       a private copy and a value Noto writes can stay invisible. That run is INVALID before anything else happens.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-LoginStart.ps1 -NotoExe <path>\Noto.exe
  pwsh -NoProfile -File tools/validation/Invoke-InShell.ps1 -NotoExe <path>\Noto.exe Invoke-LoginStart -RealRegistry
#>
param([Parameter(Mandatory)] [string] $NotoExe, [switch] $RealRegistry)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path

if (-not ('NotoVal.LoginWatch' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text; using System.Threading;
namespace NotoVal {
public static class LoginWatch {
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder b, int n);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
    static Thread thread, fgThread; static uint tid; static WinEventProc keep; static volatile bool stopFg;
    static readonly List<KeyValuePair<uint, string>> shows = new List<KeyValuePair<uint, string>>();
    static readonly List<KeyValuePair<uint, string>> fg = new List<KeyValuePair<uint, string>>();
    /// Starts recording every top-level window shown or uncloaked on the desktop, and every foreground change.
    public static void Start() {
        lock (shows) shows.Clear(); lock (fg) fg.Clear();
        var ready = new ManualResetEventSlim();
        thread = new Thread(() => {
            tid = GetCurrentThreadId();
            keep = (hook, ev, hwnd, obj, child, t, time) => {
                if (obj != 0 || hwnd == IntPtr.Zero || GetAncestor(hwnd, 2) != hwnd) return;
                uint p; GetWindowThreadProcessId(hwnd, out p); var c = new StringBuilder(128); GetClassName(hwnd, c, 128);
                lock (shows) shows.Add(new KeyValuePair<uint, string>(p, string.Format("ev=0x{0:X4} class={1}", ev, c)));
            };
            IntPtr h1 = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, keep, 0, 0, 0);   // EVENT_OBJECT_SHOW
            IntPtr h2 = SetWinEventHook(0x8018, 0x8018, IntPtr.Zero, keep, 0, 0, 0);   // EVENT_OBJECT_UNCLOAKED
            ready.Set(); MSG m; while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWinEvent(h1); UnhookWinEvent(h2);
        });
        thread.IsBackground = true; thread.Start(); ready.Wait();
        stopFg = false;
        fgThread = new Thread(() => { IntPtr last = new IntPtr(-1); while (!stopFg) { IntPtr h = GetForegroundWindow(); if (h != last) { last = h; uint p; GetWindowThreadProcessId(h, out p); var c = new StringBuilder(128); GetClassName(h, c, 128); lock (fg) fg.Add(new KeyValuePair<uint, string>(p, c.ToString())); } Thread.Sleep(2); } });
        fgThread.IsBackground = true; fgThread.Start();
    }
    public static void Stop() { PostThreadMessage(tid, 0x0012, IntPtr.Zero, IntPtr.Zero); thread.Join(2000); stopFg = true; fgThread.Join(); }
    public static string[] ShowsOf(uint pid) { lock (shows) return shows.FindAll(x => x.Key == pid).ConvertAll(x => x.Value).ToArray(); }
    public static string[] ForegroundOf(uint pid) { lock (fg) return fg.FindAll(x => x.Key == pid).ConvertAll(x => x.Value).ToArray(); }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr m, uint id, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr m, uint id, StringBuilder s, int n, uint flags);
    /// The open menu's Start Noto with Windows row (id 5): "E"/"D" enabled or grayed, "C" checked, and its text.
    public static string LoginRow(IntPtr menuWindow) {
        IntPtr h = SendMessage(menuWindow, 0x01E1, IntPtr.Zero, IntPtr.Zero);   // MN_GETHMENU
        if (h == IntPtr.Zero) return "no-menu";
        uint s = GetMenuState(h, 5, 0); if (s == 0xFFFFFFFF) return "absent";
        var t = new StringBuilder(256); GetMenuString(h, 5, t, 256, 0);
        return (((s & 3) != 0) ? "D" : "E") + (((s & 8) != 0) ? "C" : "") + "|" + t.ToString().Replace("&", "");
    }
}
}
'@
}

$run = New-ValidationRun 'login-start'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]; $Scope = [System.Windows.Automation.TreeScope]
$RunPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$ApprovedPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'

# ---------------------------------------------------------------- helpers

function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function FgPid { $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId((Fg), [ref]$p); [int]$p }
function Ev { Get-WindowEvidence $noto }
function State($e) { "visible=$($e.WsVisible) minimized=$($e.Minimized) cloaked=$($e.Cloaked) fg=$($e.IsForeground) focus=$($e.KeyboardFocus) frame=$($e.Frame)" }
function Is-Front($e) { $e.WsVisible -and -not $e.Minimized -and $e.Cloaked -eq 0 -and $e.IsForeground -and $e.KeyboardFocus -and (Test-DockedAt $e 'Right') }
function Trace-Of([int] $ProcessId) { @($log.Lines($ProcessId) | ForEach-Object { $_ -replace '^\S+ pid=\d+ ', '' }) -join ' | ' }
function Trace-Mark { @($log.Lines($noto.Pid)).Count }
function Trace-Since([int] $Mark) { @($log.Lines($noto.Pid) | Select-Object -Skip $Mark | ForEach-Object { $_ -replace '^\S+ pid=\d+ ', '' }) -join ' | ' }
function Wait-Trace([int] $ProcessId, [string] $Pattern, [int] $Seconds = 30) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if ((Trace-Of $ProcessId) -match $Pattern) { return $true }; Start-Sleep -Milliseconds 100 }
    $false
}
function Describe-At([int] $X, [int] $Y) {
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $X; $pt.Y = $Y
    $root = [NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2)
    $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId($root, [ref]$p)
    "class '$([NotoVal.Instance]::RootClassAt($X, $Y))' title '$([NotoVal.Win32]::Text($root))' process $((Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName) ($p)"
}
function Click-Class([int] $X, [int] $Y, [string[]] $Classes) {
    if ($Classes -notcontains [NotoVal.Instance]::RootClassAt($X, $Y)) { throw "refusing to click at ($X,$Y): expected $($Classes -join ' or '), found $(Describe-At $X $Y)" }
    [void][NotoVal.Win32]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 200
    if ($Classes -notcontains [NotoVal.Instance]::RootClassAt($X, $Y)) { throw "the window under the pointer at ($X,$Y) changed to $(Describe-At $X $Y)" }
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
}
function Centre($Rect) { @([int]($Rect.X + $Rect.Width / 2), [int]($Rect.Y + $Rect.Height / 2)) }
function Buttons([string] $HostClass) {
    $h = [NotoVal.Win32]::FindWindow($HostClass, [NullString]::Value)
    if ($h -eq [IntPtr]::Zero) { return @() }
    @($AE::FromHandle($h).FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
}
function Flyout { $h = [NotoVal.Win32]::FindWindow('TopLevelWindowForOverflowXamlIsland', [NullString]::Value); if ($h -ne [IntPtr]::Zero -and [NotoVal.Win32]::IsWindowVisible($h)) { $h } else { [IntPtr]::Zero } }
function Chevron { Buttons 'Shell_TrayWnd' | Where-Object { $_.Current.Name -match 'Hidden Icons' -and $_.Current.BoundingRectangle.Width -gt 0 } | Select-Object -First 1 }
function Open-Overflow {
    if ((Flyout) -ne [IntPtr]::Zero) { return }
    $xy = Centre (Chevron).Current.BoundingRectangle; Click-Class $xy[0] $xy[1] @('Shell_TrayWnd')
    $deadline = (Get-Date).AddSeconds(3); while ((Get-Date) -lt $deadline -and (Flyout) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    if ((Flyout) -eq [IntPtr]::Zero) { throw 'the overflow did not open' }
}
function Noto-Icon { Open-Overflow; $i = Buttons 'TopLevelWindowForOverflowXamlIsland' | Where-Object { $_.Current.Name -eq 'Noto' } | Select-Object -First 1; if (-not $i) { throw 'no Noto icon in the overflow' }; $i }
function Keys-ToShell([int[]] $Mods, [int] $Key) {
    if ((FgPid) -ne (Get-ShellPid)) { throw 'refusing keys: the shell does not own the foreground' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 1200
}
function Close-Flyout {
    if ((Flyout) -eq [IntPtr]::Zero) { return }
    if ((FgPid) -eq (Get-ShellPid)) { Keys-ToShell @() 0x1B }
    if ((Flyout) -ne [IntPtr]::Zero) { $xy = Centre (Chevron).Current.BoundingRectangle; Click-Class $xy[0] $xy[1] @('Shell_TrayWnd') }
    if ((Flyout) -ne [IntPtr]::Zero) { throw 'the overflow did not close' }
}
function Menu-Window { $h = [NotoVal.Win32]::FindWindow('#32768', [NullString]::Value); if ($h -ne [IntPtr]::Zero -and [NotoVal.Win32]::IsWindowVisible($h)) { $h } else { [IntPtr]::Zero } }
function Open-Menu {
    $i = Noto-Icon; $i.SetFocus(); Start-Sleep -Milliseconds 400
    if ($AE::FocusedElement.Current.Name -ne 'Noto') { throw 'the Noto icon did not take focus' }
    Keys-ToShell @(0x10) 0x79
    $deadline = (Get-Date).AddSeconds(3); while ((Get-Date) -lt $deadline -and (Menu-Window) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    if ((Menu-Window) -eq [IntPtr]::Zero) { throw 'the menu did not open' }
    [NotoVal.LoginWatch]::LoginRow((Menu-Window))
}
function Keys-ToNotoMenu([int] $Key) {
    if ((FgPid) -ne $noto.Pid -or (Menu-Window) -eq [IntPtr]::Zero) { throw 'refusing keys: Noto''s menu is not open in front' }
    [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true))); Start-Sleep -Milliseconds 1200
}
function Ctrl([string] $Label) {
    $neg = Test-NegativeControl $run $bystander.Hwnd
    $run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $run "$Label negative control was granted the foreground: $($neg.Detail)" }
}
function Target-Front {
    # Raised first, as Set-ForegroundByKeyboard does; whatever still covers the point then is named.
    [void][NotoVal.Win32]::SetWindowPos($target.Hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x13); [void][NotoVal.Win32]::SetWindowPos($target.Hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x13)
    Start-Sleep -Milliseconds 200
    $r = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($target.Hwnd, [ref]$r)
    $x = [int](($r.Left + $r.Right) / 2); $y = [int]($r.Top + 80)
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $x; $pt.Y = $y
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $target.Hwnd) { throw "the target is covered at ($x,$y) by $(Describe-At $x $y)" }
    $null = Set-ForegroundByKeyboard $target
}
function Focus-Shell {
    $mi = New-Object NotoVal.Win32+MONITORINFO; $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
    [void][NotoVal.Win32]::GetMonitorInfo([NotoVal.Win32]::MonitorFromWindow([NotoVal.Win32]::FindWindow('Shell_TrayWnd', [NullString]::Value), 1), [ref]$mi)
    $w = $mi.rcWork
    foreach ($fx in 0.35, 0.5, 0.25) { foreach ($fy in 0.92, 0.8) {
        $x = [int]($w.Left + ($w.Right - $w.Left) * $fx); $y = [int]($w.Top + ($w.Bottom - $w.Top) * $fy)
        if (@('Progman', 'WorkerW') -notcontains [NotoVal.Instance]::RootClassAt($x, $y)) { continue }
        Click-Class $x $y @('Progman', 'WorkerW')
        if ((FgPid) -eq (Get-ShellPid)) { return }
    } }
    throw 'could not put the shell in front with a desktop click'
}
function Folder-Loaded { [bool]$AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, 'FolderList')) }

function Start-Login {
    <# A --login start, watched from before it begins: returns the process and what it showed and held. #>
    [NotoVal.LoginWatch]::Start()
    try {
        $p = Start-ViaShell $run -File $NotoExe -Arguments "--data-root=`"$data`" --login" -Marker "--data-root=`"$data`""
        $ready = Wait-Trace $p.Pid 'Noto\.Tray added'
        Start-Sleep -Seconds 3
    }
    finally { [NotoVal.LoginWatch]::Stop() }
    $main = [NotoVal.Win32]::TopWindows([uint32]$p.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1
    $tray = [NotoVal.Win32]::TopWindows([uint32]$p.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto tray' } | Select-Object -First 1
    [pscustomobject]@{ Pid = $p.Pid; Hwnd = $main; Tray = $tray; Ready = $ready; DataRoot = $data
        Shows = @([NotoVal.LoginWatch]::ShowsOf([uint32]$p.Pid)); Fg = @([NotoVal.LoginWatch]::ForegroundOf([uint32]$p.Pid)) }
}
function Assert-HiddenStart([string] $Label, $N) {
    $e = Get-WindowEvidence $N
    Add-Result $run "$Label --login start -> no Noto window ever shown or uncloaked" ($N.Ready -and $N.Shows.Count -eq 0) "ready=$($N.Ready) shows=$($N.Shows -join ' ; ')"
    Add-Result $run "$Label ... never in front at any moment" ($N.Fg.Count -eq 0) "foreground entries=$($N.Fg -join ' ; ')"
    Add-Result $run "$Label ... the workspace hidden; no visible Noto window" ((-not $e.WsVisible) -and @([NotoVal.Win32]::TopWindows([uint32]$N.Pid) | Where-Object { [NotoVal.Win32]::IsWindowVisible($_) }).Count -eq 0) (State $e)
    Add-Result $run "$Label ... owns the root and added its icon" ((Trace-Of $N.Pid) -match 'login-claim owner=True' -and (Trace-Of $N.Pid) -match 'Noto\.Tray added=True') "trace: $(Trace-Of $N.Pid)"
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
function Same($A, $B) { ($A | ConvertTo-Json -Compress) -eq ($B | ConvertTo-Json -Compress) }
function Real-View([string] $Path, [string] $Name) {
    # The key as a process the shell started sees it: the user's real hive.
    Start-ShortLived $run -Script 'registry-probe.ps1' -Out (Join-Path $run.Root "registry-$Name.txt") -Arguments "-Key `"$Path`""
}
function Assert-RealHive {
    <# 0: a scratch value this harness writes must reach the real hive, or nothing here can be trusted to restore it. #>
    $path = "Software\Noto.Validation.RealHive-$($run.Id)"; $token = [guid]::NewGuid().ToString('N')
    $parent = (Get-Process -Id $PID).Parent
    try {
        $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path, $true)
        try { $k.SetValue('probe', $token, [Microsoft.Win32.RegistryValueKind]::String) } finally { $k.Dispose() }
        $seen = @(Real-View $path 'probe-written')
    }
    finally { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($path, $false) }
    $after = @(Real-View $path 'probe-removed')
    $context = "harness pid=$PID parent=$($parent.ProcessName)($($parent.Id)); real view of the scratch key: [$($seen -join '; ')] then [$($after -join '; ')]"
    if ($seen -notcontains "probe=String|$token") { throw "the registry this harness writes is not the user's real hive, so its snapshot and restore would not be real; run it through Invoke-InShell.ps1. $context" }
    if ($after -notcontains '<key>=absent') { throw "the scratch key $path is still in the real hive after cleanup. $context" }
    Add-Result $run '0  the harness reads and writes the real hive: its scratch value seen by a shell-started process, then gone' $true $context
}

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title 'Run at login: the hidden start' -Body {
        if ($RealRegistry) { Assert-RealHive }
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $script:data = Join-Path $run.Root 'root'; New-Item -ItemType Directory $data | Out-Null
        $seed = Start-Noto $run -Exe $NotoExe -DataRoot $data; if (-not (Stop-Noto $seed)) { throw 'the seeding start did not exit cleanly' }
        $script:target = Start-TargetApp $run
        $script:bystander = Start-TargetApp $run 'bystander'
        $pipeName = [NotoVal.Instance]::PipeName($data)

        # 1-3
        foreach ($i in 1..3) {
            Target-Front
            $script:noto = Start-Login
            Assert-HiddenStart "1.$i" $noto
            if ($i -eq 1) {
                Target-Front; Ctrl '2 before the hotkey'
                $m = Trace-Mark; Send-ActivationChord -AllowedForeground $target.Hwnd; Start-Sleep -Seconds 1
                $e = Ev
                Add-Result $run '2  the hotkey after a --login start -> shown, docked, in front, focused, folders loaded' ((Is-Front $e) -and (Folder-Loaded)) "$(State $e); trace: $(Trace-Since $m)"
                Ctrl '2 after the hotkey'
                $null = Invoke-Minimize $noto; Target-Front
                Send-ActivationChord -AllowedForeground $target.Hwnd; Start-Sleep -Seconds 1
                Add-Result $run '3  minimize, then the hotkey -> restored at the docked rectangle' (Is-Front (Ev)) (State (Ev))
            }
            if (-not (Stop-Noto $noto)) { throw 'Noto did not exit cleanly' }
        }

        # 4
        Target-Front; $script:noto = Start-Login
        Target-Front; Ctrl '4 before the click'
        $m = Trace-Mark; $xy = Centre (Noto-Icon).Current.BoundingRectangle; Click-Class $xy[0] $xy[1] @('TopLevelWindowForOverflowXamlIsland'); Start-Sleep -Seconds 1
        Add-Result $run '4  a tray click after a --login start -> shown, docked, in front (a launch)' ((Is-Front (Ev)) -and (Trace-Since $m) -match 'request=Launch action=Show') "$(State (Ev)); trace: $(Trace-Since $m)"
        Ctrl '4 after the click'
        Close-Flyout
        if (-not (Stop-Noto $noto)) { throw 'Noto did not exit cleanly' }

        # 5
        Target-Front; $script:noto = Start-Login
        Target-Front; Ctrl '5 before the desktop click'
        Focus-Shell
        $m = Trace-Mark
        $view = [NotoVal.ShellLaunch]::DesktopViewDispatch(); $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`"", $run.Root, 'open', 1)
        Start-Sleep -Seconds 4
        Add-Result $run '5  a plain second launch after a desktop click -> shown, docked, in front (a launch)' ((Is-Front (Ev)) -and (Trace-Since $m) -match 'Noto\.Instance launch action=Show' -and @(Get-Process -Name Noto).Count -eq 1) "$(State (Ev)); trace: $(Trace-Since $m)"
        Ctrl '5 after the launch'

        # 6 - the owner is shown now; then hidden by the hotkey
        foreach ($phase in 'shown', 'hidden') {
            if ($phase -eq 'hidden') { if ((Fg) -ne $noto.Hwnd) { Target-Front; Send-ActivationChord -AllowedForeground $target.Hwnd; Start-Sleep -Milliseconds 800 }; Send-ActivationChord -AllowedForeground $noto.Hwnd; Start-Sleep -Milliseconds 800 }
            $before = Ev; $m = Trace-Mark
            [NotoVal.LoginWatch]::Start()
            try {
                $view = [NotoVal.ShellLaunch]::DesktopViewDispatch(); $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`" --login", $run.Root, 'open', 1)
                Start-Sleep -Seconds 4
            }
            finally { [NotoVal.LoginWatch]::Stop() }
            $after = Ev
            $others = @($log.All() | Where-Object { $_ -match 'login-claim owner=False' -and $_ -notmatch " pid=$($noto.Pid) " })
            Add-Result $run "6  --login while an owner runs ($phase) -> it exits; no handoff; the owner unchanged" ($others.Count -ge 1 -and @(Get-Process -Name Noto).Count -eq 1 -and (Trace-Since $m) -notmatch 'launch action|request=' -and $before.WsVisible -eq $after.WsVisible -and $before.IsForeground -eq $after.IsForeground) "second: $($others[-1] -replace '^\S+ ', ''); owner $(State $after); owner trace: $(Trace-Since $m)"
            Add-Result $run "6  ... nothing of the owner shown by it ($phase)" (@([NotoVal.LoginWatch]::ShowsOf([uint32]$noto.Pid)).Count -eq 0) "shows=$([NotoVal.LoginWatch]::ShowsOf([uint32]$noto.Pid) -join ' ; ')"
        }
        if (-not (Stop-Noto $noto)) { throw 'Noto did not exit cleanly' }

        # 7-8: the row, read-only; then Quit a login start never shown
        Target-Front; $script:noto = Start-Login
        $expected = if ((Registry-Snapshot $RunPath).Contains('Noto')) { 'present' } else { 'off' }
        Target-Front
        $row = Open-Menu
        Keys-ToNotoMenu 0x1B
        Add-Result $run '7  Start Noto with Windows reads as off: enabled, unchecked (Windows has no registration here)' ($expected -eq 'off' -and $row -eq 'E|Start Noto with Windows') "row=$row registration=$expected"
        Close-Flyout
        Target-Front
        $old = $noto; $null = Open-Menu; Keys-ToNotoMenu 0x51   # Q
        $p = Get-Process -Id $old.Pid -ErrorAction SilentlyContinue; $gone = (-not $p) -or $p.WaitForExit(10000)
        Start-Sleep -Milliseconds 500
        $t = Trace-Of $old.Pid
        Add-Result $run '8  Quit a --login start never shown -> exits; mutex and pipe gone; one shutdown' ($gone -and -not [NotoVal.Instance]::MutexExists($pipeName) -and -not [NotoVal.Instance]::PipeExists($pipeName) -and ([regex]::Matches($t, 'shutting-down')).Count -eq 1) "trace: $t"
        Close-Flyout

        if ($RealRegistry) {
            # 9 - the one approved round trip on the real registration, restored whatever happens
            $runBefore = Registry-Snapshot $RunPath; $approvedBefore = Registry-Snapshot $ApprovedPath
            $approvedReal = Real-View $ApprovedPath 'approved-before'
            if ($runBefore.Contains('Noto')) { throw 'a Run\Noto value already exists; refusing to touch it' }
            $expectedCommand = "`"$NotoExe`" --login"
            try {
                Target-Front; $script:noto = Start-Login
                Target-Front; $null = Open-Menu; Keys-ToNotoMenu 0x57   # W: Start Noto with Windows
                Close-Flyout
                # Read only once Noto says it has written: the choice can run after the keystroke's wait has ended.
                $null = Wait-Trace $noto.Pid 'Noto\.Login before=Off after=On' 10
                $value = (Registry-Snapshot $RunPath)['Noto']
                $real = @(Real-View $RunPath 'run-written') -match '^Noto='
                Target-Front; $row = Open-Menu; Keys-ToNotoMenu 0x1B; Close-Flyout
                Add-Result $run '9  choosing it registers exactly "<this exe>" --login; the row reads checked' ($value -eq "String|$expectedCommand" -and "$real" -eq "Noto=String|$expectedCommand" -and $row -eq 'EC|Start Noto with Windows') "value=$value real view=$real row=$row; trace: $(Trace-Of $noto.Pid)"
                Target-Front; $null = Open-Menu; Keys-ToNotoMenu 0x57
                Close-Flyout
                $null = Wait-Trace $noto.Pid 'Noto\.Login before=On after=Off' 10
                $runMid = Registry-Snapshot $RunPath
                Add-Result $run '9  choosing it again removes it' (-not $runMid.Contains('Noto') -and (Trace-Of $noto.Pid) -match 'Noto\.Login before=On after=Off') "trace: $(Trace-Of $noto.Pid)"
                Add-Result $run '9  Windows'' Startup apps setting byte-identical throughout (never written)' (Same $approvedBefore (Registry-Snapshot $ApprovedPath)) ''
            }
            finally {
                $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($RunPath, $true)
                try { if (-not $runBefore.Contains('Noto')) { $k.DeleteValue('Noto', $false) } } finally { $k.Dispose() }
                $runReal = @(Real-View $RunPath 'run-restored') -join "`n"; $approvedRealAfter = @(Real-View $ApprovedPath 'approved-after')
                $runBeforeLines = @($runBefore.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n"
                Add-Result $run '9  restored: Run and StartupApproved identical to the snapshot, here and in the real view' ((Same $runBefore (Registry-Snapshot $RunPath)) -and (Same $approvedBefore (Registry-Snapshot $ApprovedPath)) -and $runReal -eq $runBeforeLines -and ($approvedRealAfter -join "`n") -eq ($approvedReal -join "`n")) "Run values $((Registry-Snapshot $RunPath).Count); real Run identical=$($runReal -eq $runBeforeLines); real StartupApproved identical=$(($approvedRealAfter -join "`n") -eq ($approvedReal -join "`n")) ($($approvedReal.Count) values)"
            }
        }
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
}
exit $code
