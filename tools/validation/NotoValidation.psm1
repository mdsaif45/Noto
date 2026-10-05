#Requires -Version 7.4
# Noto runtime-validation harness: foreground-correct, isolated, self-cleaning.
# See README.md for why each rule exists. Nothing here touches a real Noto data root.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ('NotoVal.Win32' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NotoVal
{
    public static class Win32
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public ushort wVk; [FieldOffset(10)] public ushort wScan; [FieldOffset(12)] public uint dwFlags; [FieldOffset(16)] public uint time; [FieldOffset(24)] public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        public delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);

        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO g);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

        public static string Text(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
        public static IntPtr[] TopWindows(uint pid)
        {
            var list = new List<IntPtr>();
            EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) list.Add(h); return true; }, IntPtr.Zero);
            return list.ToArray();
        }
        public static void Keys(INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }
        public static INPUT Vk(ushort vk, bool up) { var i = new INPUT(); i.type = 1; i.wVk = vk; i.dwFlags = up ? 2u : 0u; return i; }
        public static INPUT Uni(char c, bool up) { var i = new INPUT(); i.type = 1; i.wScan = c; i.dwFlags = 4u | (up ? 2u : 0u); return i; }
    }

    // Launching through the running shell: the desktop view's IShellFolderViewDual.Application.ShellExecute
    // runs inside explorer, so the new process is explorer's child, not this harness's.
    public static class ShellLaunch
    {
        [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")] class ShellWindowsClass { }
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IShellWindows
        {
            int Count { get; }
            [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
            [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
            void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int cookie);
            void RegisterPending(int threadId, ref object loc, ref object locRoot, int swClass, out int cookie);
            void Revoke(int cookie);
            void OnNavigate(int cookie, ref object loc);
            void OnActivated(int cookie, bool active);
            [return: MarshalAs(UnmanagedType.IDispatch)] object FindWindowSW(ref object loc, ref object locRoot, int swClass, out int hwnd, int options);
        }
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider { [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService(ref Guid service, ref Guid riid); }
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser
        {
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
            void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject();
            void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            [return: MarshalAs(UnmanagedType.Interface)] IShellView QueryActiveShellView();
        }
        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellView
        {
            void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless(); void UIActivate();
            void Refresh(); void CreateViewWindow(); void DestroyViewWindow(); void GetCurrentInfo(); void AddPropertySheetPages();
            void SaveViewState(); void SelectItem();
            [return: MarshalAs(UnmanagedType.IDispatch)] object GetItemObject(uint item, ref Guid riid);
        }
        public static object DesktopViewDispatch()
        {
            var sw = (IShellWindows)new ShellWindowsClass();
            object loc = 0; object root = Type.Missing; int hwnd;
            object disp = sw.FindWindowSW(ref loc, ref root, 8, out hwnd, 1);
            var sp = (IServiceProvider)disp;
            Guid sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid iid = typeof(IShellBrowser).GUID;
            var browser = (IShellBrowser)sp.QueryService(ref sid, ref iid);
            Guid idisp = new Guid("00020400-0000-0000-C000-000000000046");
            return browser.QueryActiveShellView().GetItemObject(0, ref idisp);
        }
    }
}
'@
}
[void][NotoVal.Win32]::SetThreadDpiAwarenessContext([IntPtr](-4))

$script:Helpers = Join-Path $PSScriptRoot 'helpers'
$script:VdKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops'
$script:Base = Join-Path ([IO.Path]::GetTempPath()) 'noto-validation'
$VK = @{ CTRL = 0x11; ALT = 0x12; SHIFT = 0x10; LWIN = 0x5B; SPACE = 0x20; D = 0x44; LEFT = 0x25; RIGHT = 0x27; F4 = 0x73 }

# ------------------------------------------------------------------ run context

function New-ValidationRun {
    param([Parameter(Mandatory)] [string] $Name)
    $id = '{0}-{1:yyyyMMdd-HHmmss}-{2}' -f $Name, (Get-Date), ([guid]::NewGuid().ToString('N').Substring(0, 6))
    $root = Join-Path $script:Base $id
    New-Item -ItemType Directory $root -Force | Out-Null
    [pscustomobject]@{ Id = $id; Root = $root; Started = [System.Collections.Generic.List[object]]::new(); Seen = [System.Collections.Generic.HashSet[int]]::new(); Results = [System.Collections.Generic.List[object]]::new(); Invalid = [System.Collections.Generic.List[string]]::new(); Controls = [System.Collections.Generic.List[object]]::new(); Desktop = $null }
}

function Assert-UnderRun {
    param($Run, [string] $Path)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($Run.Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to touch '$full': outside this run's isolated root $($Run.Root)"
    }
}

function Add-Result {
    param($Run, [string] $Case, [bool] $Pass, [string] $Detail, $Evidence = $null)
    $Run.Results.Add([pscustomobject]@{ Case = $Case; Pass = $Pass; Detail = $Detail; Evidence = $Evidence })
    Write-Host ('  [{0}] {1,-62} {2}' -f ($(if ($Pass) { 'PASS' } else { 'FAIL' })), $Case, $Detail)
}

function Add-Invalid {
    <# A precondition of the method itself failed: the run proves nothing, whatever its checks say. #>
    param($Run, [string] $Why)
    $Run.Invalid.Add($Why)
    Write-Host "  [INVALID] $Why"
}

function Complete-ValidationRun {
    <# Writes results.json under the run root, prints the verdict and returns the exit code: 0 pass, 1 fail, 2 invalid. #>
    param($Run, [string] $Title)
    $failed = @($Run.Results | Where-Object { -not $_.Pass })
    $verdict = if ($Run.Invalid.Count) { 'INVALID' } elseif ($failed.Count) { "$($failed.Count) FAILED" } else { 'ALL PASS' }
    [pscustomobject]@{ Run = $Run.Id; Title = $Title; Verdict = $verdict; Invalid = @($Run.Invalid); Controls = @($Run.Controls); Results = @($Run.Results) } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Run.Root 'results.json')
    Write-Host "=== $Title"
    $refused = @($Run.Controls | Where-Object Refused).Count
    Write-Host "  $($Run.Results.Count) checks, $($failed.Count) failed, $($Run.Invalid.Count) invalid; negative controls refused $refused/$($Run.Controls.Count) -> $verdict"
    Write-Host "  evidence: $(Join-Path $Run.Root 'results.json')"
    if ($Run.Invalid.Count) { 2 } elseif ($failed.Count) { 1 } else { 0 }
}

# ------------------------------------------------------------------ processes

# A started process is identified by pid AND start time, so a pid Windows has since reused for an unrelated
# process is never mistaken for one of ours, and never stopped.
function Add-Started {
    param($Run, [int] $ProcessId, [Nullable[long]] $StartTicks)
    $Run.Started.Add([pscustomobject]@{ Pid = $ProcessId; Start = $StartTicks })
}

function Test-StartedAlive {
    param($Entry)
    $p = Get-Process -Id $Entry.Pid -ErrorAction SilentlyContinue
    if (-not $p -or $null -eq $Entry.Start) { return $false }
    try { $p.StartTime.ToUniversalTime().Ticks -eq $Entry.Start } catch { $false }
}

function Get-ShellPid {
    $tray = [NotoVal.Win32]::FindWindow('Shell_TrayWnd', [NullString]::Value)
    $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId($tray, [ref]$p); [int]$p
}

function Start-ViaShell {
    <# Starts a process from inside the running shell and returns it once seen. The marker must be unique to this start. #>
    param($Run, [Parameter(Mandatory)] [string] $File, [string] $Arguments = '', [Parameter(Mandatory)] [string] $Marker, [int] $Show = 1, [int] $TimeoutSeconds = 30)
    $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
    $view.Application.ShellExecute($File, $Arguments, $Run.Root, 'open', $Show)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $p = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($Marker) -and -not $Run.Seen.Contains([int]$_.ProcessId) } | Select-Object -First 1
        if ($p) {
            $shell = Get-ShellPid
            if ([int]$p.ParentProcessId -ne $shell) { Stop-Process -Id $p.ProcessId -Force; throw "process $($p.ProcessId) was not started by the shell (parent $($p.ParentProcessId), shell $shell)" }
            $start = try { (Get-Process -Id $p.ProcessId -ErrorAction Stop).StartTime.ToUniversalTime().Ticks } catch { $null }
            [void]$Run.Seen.Add([int]$p.ProcessId); Add-Started $Run ([int]$p.ProcessId) $start
            return [pscustomobject]@{ Pid = [int]$p.ProcessId; Parent = [int]$p.ParentProcessId }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "no process with marker '$Marker' appeared within ${TimeoutSeconds}s"
}

function Start-ShortLived {
    <#
      A helper that may finish before it can be seen in the process list. It writes "pid=<n> parent=<n> start=<ticks>" as its
      first line and its result after; the parent is checked against the shell here. Returns the result lines.
    #>
    param($Run, [Parameter(Mandatory)] [string] $Script, [Parameter(Mandatory)] [string] $Out, [string] $Arguments = '')
    $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
    $view.Application.ShellExecute('pwsh.exe', "-NoProfile -WindowStyle Hidden -File `"$(Join-Path $script:Helpers $Script)`" -Out `"$Out`" $Arguments", $Run.Root, 'open', 0)
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and -not ((Get-Content $Out -ErrorAction SilentlyContinue) -match '^done$')) { Start-Sleep -Milliseconds 200 }
    $lines = @(Get-Content $Out -ErrorAction SilentlyContinue)
    if (-not ($lines -match '^done$')) { throw "helper $Script did not finish within 30s" }
    if ($lines[0] -notmatch '^pid=(\d+) parent=(\d+) start=(\d+)$') { throw "helper $Script did not report its pid" }
    $procId = [int]$Matches[1]; $parent = [int]$Matches[2]; $start = [long]$Matches[3]
    if ($parent -ne (Get-ShellPid)) { throw "helper $Script pid $procId was not started by the shell (parent $parent)" }
    if (-not $Run.Seen.Add($procId)) { throw "helper $Script reported a pid already seen in this run: $procId" }
    Add-Started $Run $procId $start
    @($lines[1..($lines.Count - 2)])
}

function Start-Noto {
    param($Run, [Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $DataRoot)
    Assert-UnderRun $Run $DataRoot
    $p = Start-ViaShell $Run -File $Exe -Arguments "--data-root=`"$DataRoot`"" -Marker "--data-root=`"$DataRoot`""
    $deadline = (Get-Date).AddSeconds(30); $h = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline -and $h -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 250
        $h = [NotoVal.Win32]::TopWindows([uint32]$p.Pid) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' -and [NotoVal.Win32]::IsWindowVisible($_) } | Select-Object -First 1
        if (-not $h) { $h = [IntPtr]::Zero }
        if (-not (Get-Process -Id $p.Pid -ErrorAction SilentlyContinue)) { throw "Noto $($p.Pid) exited during start-up" }
    }
    if ($h -eq [IntPtr]::Zero) { throw "Noto $($p.Pid) showed no window" }
    Start-Sleep -Seconds 2   # docking completes after the first frame
    [pscustomobject]@{ Pid = $p.Pid; Hwnd = $h; Parent = $p.Parent; DataRoot = $DataRoot }
}

function Stop-Noto {
    <# WM_CLOSE works whether the window is visible or minimized. Returns $true for a clean exit. #>
    param($Noto)
    $proc = Get-Process -Id $Noto.Pid -ErrorAction SilentlyContinue
    if (-not $proc) { return $true }
    [void][NotoVal.Win32]::PostMessage($Noto.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    if ($proc.WaitForExit(10000)) { return $true }
    Stop-Process -Id $Noto.Pid -Force; return $false
}

function Stop-RunProcesses {
    <# Stops every process this run started that is still the same process, then reports whether none is left. #>
    param($Run)
    foreach ($entry in $Run.Started) { if (Test-StartedAlive $entry) { Stop-Process -Id $entry.Pid -Force -ErrorAction SilentlyContinue } }
    Start-Sleep -Milliseconds 500
    @($Run.Started | Where-Object { Test-StartedAlive $_ }).Count -eq 0
}

# ------------------------------------------------------------------ settings in an isolated database

function Initialize-NotoSqlite {
    param([Parameter(Mandatory)] [string] $Exe)
    if ('Microsoft.Data.Sqlite.SqliteConnection' -as [type]) { return }
    $dir = Split-Path $Exe
    foreach ($a in 'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'SQLitePCLRaw.batteries_v2.dll', 'Microsoft.Data.Sqlite.dll') { Add-Type -Path (Join-Path $dir $a) }
    $env:PATH = "$dir;$env:PATH"   # e_sqlite3.dll
    [SQLitePCL.Batteries_V2]::Init()
}

function Set-NotoSetting {
    param($Run, [Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $DataRoot, [Parameter(Mandatory)] [string] $Key, [Parameter(Mandatory)] [string] $Value)
    $db = Join-Path $DataRoot 'noto.db'; Assert-UnderRun $Run $db
    if (-not (Test-Path $db)) { throw "no database at ${db}: start and close Noto on this root first" }
    Initialize-NotoSqlite $Exe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = 'INSERT INTO Settings (Key, Value) VALUES ($k, $v) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value'; [void]$cmd.Parameters.AddWithValue('$k', $Key); [void]$cmd.Parameters.AddWithValue('$v', $Value); [void]$cmd.ExecuteNonQuery() }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}

function Get-NotoSettings {
    param($Run, [Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $DataRoot)
    $db = Join-Path $DataRoot 'noto.db'; Assert-UnderRun $Run $db
    if (-not (Test-Path $db)) { return '{}' }
    Initialize-NotoSqlite $Exe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db;Mode=ReadOnly"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT Key, Value FROM Settings ORDER BY Key'; $r = $cmd.ExecuteReader(); $m = [ordered]@{}; while ($r.Read()) { $m[$r.GetString(0)] = $r.GetString(1) }; ($m | ConvertTo-Json -Compress) }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}

function Invoke-NotoSql {
    <# Runs one statement against this run's isolated database and returns the rows affected. Noto must not be mid-write. #>
    param($Run, [Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $DataRoot, [Parameter(Mandatory)] [string] $Sql, [hashtable] $Parameters = @{})
    $db = Join-Path $DataRoot 'noto.db'; Assert-UnderRun $Run $db
    if (-not (Test-Path $db)) { throw "no database at ${db}: start and close Noto on this root first" }
    Initialize-NotoSqlite $Exe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $Sql; foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }; $cmd.ExecuteNonQuery() }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}

function Get-NotoScalar {
    <# Reads one value from this run's isolated database, read-only. #>
    param($Run, [Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $DataRoot, [Parameter(Mandatory)] [string] $Sql, [hashtable] $Parameters = @{})
    $db = Join-Path $DataRoot 'noto.db'; Assert-UnderRun $Run $db
    Initialize-NotoSqlite $Exe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db;Mode=ReadOnly"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $Sql; foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }; $cmd.ExecuteScalar() }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}

# ------------------------------------------------------------------ evidence

function Get-WindowEvidence {
    param([Parameter(Mandatory)] $Noto)
    $h = $Noto.Hwnd
    $r = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($h, [ref]$r)
    $f = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::DwmGetWindowAttribute($h, 9, [ref]$f, 16)
    $cloak = 0; [void][NotoVal.Win32]::DwmGetWindowAttribute($h, 14, [ref]$cloak, 4)
    $fg = [NotoVal.Win32]::GetForegroundWindow(); $fgPid = 0; $tid = [NotoVal.Win32]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    $g = New-Object NotoVal.Win32+GUITHREADINFO; $g.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($g); [void][NotoVal.Win32]::GetGUIThreadInfo($tid, [ref]$g)
    $mi = New-Object NotoVal.Win32+MONITORINFO; $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
    [void][NotoVal.Win32]::GetMonitorInfo([NotoVal.Win32]::MonitorFromWindow($h, 2), [ref]$mi)
    $focusRoot = if ($g.hwndFocus -ne [IntPtr]::Zero) { [NotoVal.Win32]::GetAncestor($g.hwndFocus, 2) } else { [IntPtr]::Zero }
    [pscustomobject]@{
        WsVisible = (([NotoVal.Win32]::GetWindowLong($h, -16) -band 0x10000000) -ne 0); Minimized = [NotoVal.Win32]::IsIconic($h); Cloaked = $cloak
        Outer = "($($r.Left),$($r.Top))-($($r.Right),$($r.Bottom))"; Frame = "($($f.Left),$($f.Top))-($($f.Right),$($f.Bottom))"
        FrameLeft = $f.Left; FrameRight = $f.Right; FrameTop = $f.Top; FrameBottom = $f.Bottom
        Work = "($($mi.rcWork.Left),$($mi.rcWork.Top))-($($mi.rcWork.Right),$($mi.rcWork.Bottom))"; WorkLeft = $mi.rcWork.Left; WorkRight = $mi.rcWork.Right; WorkTop = $mi.rcWork.Top; WorkBottom = $mi.rcWork.Bottom
        ForegroundPid = [int]$fgPid; IsForeground = ($fg -eq $h); KeyboardFocus = ($focusRoot -eq $h)
        FocusRootPid = $(if ($focusRoot -ne [IntPtr]::Zero) { $p2 = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId($focusRoot, [ref]$p2); [int]$p2 } else { 0 })
    }
}

function Invoke-Minimize {
    <# What the taskbar's Minimize does: WM_SYSCOMMAND SC_MINIMIZE. No foreground rights involved. #>
    param([Parameter(Mandatory)] $Noto)
    [void][NotoVal.Win32]::SendMessage($Noto.Hwnd, 0x0112, [IntPtr]0xF020, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
    [NotoVal.Win32]::IsIconic($Noto.Hwnd)
}

function Test-DockedAt {
    <# The visible frame is flush with the work area's edge and spans its height. #>
    param($Evidence, [ValidateSet('Left', 'Right')] [string] $Edge)
    $flush = if ($Edge -eq 'Left') { $Evidence.FrameLeft -eq $Evidence.WorkLeft } else { $Evidence.FrameRight -eq $Evidence.WorkRight }
    $flush -and $Evidence.FrameTop -eq $Evidence.WorkTop -and $Evidence.FrameBottom -eq $Evidence.WorkBottom
}

function Assert-ChordFree {
    <# The chord must be free before a run: a running Noto or any other holder would decide every hotkey check. #>
    param($Run)
    if (-not (Test-ChordFree)) { throw 'Ctrl+Alt+Win+Space is already registered by another process (a running Noto?); close it and rerun' }
}

function Test-ChordFree {
    # Ctrl+Alt+Win+Space, MOD_NOREPEAT: can this process register it right now?
    $ok = [NotoVal.Win32]::RegisterHotKey([IntPtr]::Zero, 0x5650, 0x2 -bor 0x1 -bor 0x8 -bor 0x4000, 0x20)
    if ($ok) { [void][NotoVal.Win32]::UnregisterHotKey([IntPtr]::Zero, 0x5650) }
    $ok
}

# ------------------------------------------------------------------ the foreground target and keyboard input

function Start-TargetApp {
    param($Run, [ValidatePattern('^[a-z0-9-]+$')] [string] $Name = 'target')
    $title = "Noto validation $Name $($Run.Id)"; $status = Join-Path $Run.Root "$Name-status.txt"
    $null = Start-ViaShell $Run -File 'pwsh.exe' -Arguments "-NoProfile -WindowStyle Hidden -File `"$(Join-Path $script:Helpers 'target-app.ps1')`" -Title `"$title`" -Status `"$status`"" -Marker $title
    $deadline = (Get-Date).AddSeconds(30); $h = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline -and $h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250; $h = [NotoVal.Win32]::FindWindow([NullString]::Value, $title) }
    if ($h -eq [IntPtr]::Zero) { throw 'the target app showed no window' }
    $p = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId($h, [ref]$p)
    [pscustomobject]@{ Hwnd = $h; Pid = [int]$p; Status = $status }
}

function Set-ForegroundByKeyboard {
    <#
      A real click into the target's text box, then real keystrokes typed into it - but only once the target is
      verifiably the foreground window, so no keystroke can reach anything else. Returns the token that landed.
    #>
    param([Parameter(Mandatory)] $Target)
    $r = New-Object NotoVal.Win32+RECT; [void][NotoVal.Win32]::GetWindowRect($Target.Hwnd, [ref]$r)
    [void][NotoVal.Win32]::SetWindowPos($Target.Hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x13); [void][NotoVal.Win32]::SetWindowPos($Target.Hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x13)
    Start-Sleep -Milliseconds 200
    $x = [int](($r.Left + $r.Right) / 2); $y = [int]($r.Top + 80)
    $pt = New-Object NotoVal.Win32+POINT; $pt.X = $x; $pt.Y = $y
    if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $Target.Hwnd) { throw "refusing to click at ($x,$y): it is not the target window" }
    [void][NotoVal.Win32]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 150
    [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $Target.Hwnd) { throw 'the target did not become the foreground window after the click; no keystroke sent' }
    $token = 'k' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    foreach ($c in $token.ToCharArray()) {
        if ([NotoVal.Win32]::GetForegroundWindow() -ne $Target.Hwnd) { throw 'the foreground changed while typing; stopped' }
        [NotoVal.Win32]::Keys(@([NotoVal.Win32]::Uni($c, $false), [NotoVal.Win32]::Uni($c, $true)))
    }
    Start-Sleep -Milliseconds 500
    if (-not ((Get-Content $Target.Status -Raw -ErrorAction SilentlyContinue) -match $token)) { throw "typed token '$token' did not reach the target's text box" }
    $token
}

function Send-ActivationChord {
    <# Ctrl+Alt+Win+Space. Sent only while one of this run's windows is in front, so an unregistered chord cannot type into another app. #>
    param([Parameter(Mandatory)] [IntPtr[]] $AllowedForeground)
    if ($AllowedForeground -notcontains [NotoVal.Win32]::GetForegroundWindow()) { throw 'refusing to send the chord: the foreground window is not one of this run''s' }
    $down = @($VK.CTRL, $VK.ALT, $VK.LWIN) | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }
    $up = @($VK.LWIN, $VK.ALT, $VK.CTRL) | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) }
    [NotoVal.Win32]::Keys(@($down) + @([NotoVal.Win32]::Vk($VK.SPACE, $false), [NotoVal.Win32]::Vk($VK.SPACE, $true)) + @($up))
    Start-Sleep -Milliseconds 1000
}

function Test-NegativeControl {
    <#
      An unrelated process, started by the shell and never given input, asks for the foreground for $Hwnd.
      Windows must refuse. If it does not, foreground rights are not controlled here and the run is invalid.
    #>
    param($Run, [Parameter(Mandatory)] [IntPtr] $Hwnd)
    $out = Join-Path $Run.Root ("negative-{0}.txt" -f [guid]::NewGuid().ToString('N').Substring(0, 6))
    $before = [NotoVal.Win32]::GetForegroundWindow()
    $line = (Start-ShortLived $Run -Script 'foreground-probe.ps1' -Out $out -Arguments "-Hwnd $([int64]$Hwnd)") -join ' '
    $after = [NotoVal.Win32]::GetForegroundWindow()
    [pscustomobject]@{ Refused = ($line -match 'sfw=False') -and ($after -eq $before); Detail = "$line; foreground unchanged=$($after -eq $before)" }
}

function Assert-ForegroundLocked {
    <#
      Runs the negative control against whichever of the two windows is NOT in front, and marks the run invalid
      if Windows grants it. Called immediately before and after every foreground claim, so each claim is
      bracketed by evidence that foreground rights were controlled at that moment.
    #>
    param($Run, [Parameter(Mandatory)] [IntPtr] $A, [Parameter(Mandatory)] [IntPtr] $B, [Parameter(Mandatory)] [string] $Label)
    $away = if ([NotoVal.Win32]::GetForegroundWindow() -eq $A) { $B } else { $A }
    $neg = Test-NegativeControl $Run $away
    $Run.Controls.Add([pscustomobject]@{ Label = $Label; Refused = $neg.Refused; Detail = $neg.Detail })
    if (-not $neg.Refused) { Add-Invalid $Run "$Label negative control was granted the foreground: $($neg.Detail)" }
    $neg
}

function Start-HotkeyHolder {
    <# Another process holding Ctrl+Alt+Win+Space until told to stop. Returns the files it reports through. #>
    param($Run)
    $ready = Join-Path $Run.Root 'holder-ready.txt'; $stop = Join-Path $Run.Root 'holder.stop'
    $null = Start-ViaShell $Run -File 'pwsh.exe' -Arguments "-NoProfile -WindowStyle Hidden -File `"$(Join-Path $script:Helpers 'hotkey-holder.ps1')`" -ReadyFile `"$ready`" -StopFile `"$stop`"" -Marker $ready -Show 0
    $deadline = (Get-Date).AddSeconds(30); while ((Get-Date) -lt $deadline -and -not (Test-Path $ready)) { Start-Sleep -Milliseconds 200 }
    [pscustomobject]@{ Ready = $ready; Stop = $stop }
}

function Stop-HotkeyHolder {
    param($Holder)
    Set-Content $Holder.Stop 'stop'
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not ((Get-Content $Holder.Ready -ErrorAction SilentlyContinue) -match '^hits=')) { Start-Sleep -Milliseconds 200 }
    (Get-Content $Holder.Ready -ErrorAction SilentlyContinue) -join ' '
}

# ------------------------------------------------------------------ virtual desktop

function Get-DesktopState {
    $v = Get-ItemProperty $script:VdKey
    $ids = [byte[]]$v.VirtualDesktopIDs; $list = for ($i = 0; $i -lt $ids.Length; $i += 16) { [guid]::new([byte[]]$ids[$i..($i + 15)]) }
    [pscustomobject]@{ Current = [guid]::new([byte[]]$v.CurrentVirtualDesktop); All = @($list) }
}

function Send-ShellChord {
    <#
      Win+Ctrl+D / Left / Right / F4: the shell's own virtual-desktop hotkeys. Windows consumes them before any
      application sees the key, so they cannot type into whatever is in front; nothing else is accepted here.
    #>
    param($Run, [int[]] $Mods, [int] $Key)
    if ((Compare-Object $Mods @($VK.LWIN, $VK.CTRL) -SyncWindow 0) -or @($VK.D, $VK.LEFT, $VK.RIGHT, $VK.F4) -notcontains $Key) { throw "not a virtual-desktop hotkey: $Mods + $Key" }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 1200
}

function Enter-TempDesktop {
    <# Every test runs on a fresh virtual desktop, so no click or keystroke can reach the developer's own windows. #>
    param($Run)
    $Run.Desktop = Get-DesktopState
    Send-ShellChord $Run @($VK.LWIN, $VK.CTRL) $VK.D
    $now = Get-DesktopState
    if ($now.Current -eq $Run.Desktop.Current) { throw 'no temporary virtual desktop was created' }
    $Run.Desktop | Add-Member -NotePropertyName Temp -NotePropertyValue $now.Current -Force
}

function Step-ToDesktop($Run, [guid] $Target) {
    for ($i = 0; $i -lt 8 -and (Get-DesktopState).Current -ne $Target; $i++) {
        $s = Get-DesktopState
        if ([array]::IndexOf($s.All, $Target) -gt [array]::IndexOf($s.All, $s.Current)) { Send-ShellChord $Run @($VK.LWIN, $VK.CTRL) $VK.RIGHT } else { Send-ShellChord $Run @($VK.LWIN, $VK.CTRL) $VK.LEFT }
    }
}

function Exit-TempDesktop {
    param($Run)
    if (-not $Run.Desktop -or -not ($Run.Desktop.PSObject.Properties.Name -contains 'Temp')) { return $true }
    if ((Get-DesktopState).All -contains $Run.Desktop.Temp) {
        Step-ToDesktop $Run $Run.Desktop.Temp
        if ((Get-DesktopState).Current -eq $Run.Desktop.Temp) { Send-ShellChord $Run @($VK.LWIN, $VK.CTRL) $VK.F4 }
    }
    Step-ToDesktop $Run $Run.Desktop.Current
    $s = Get-DesktopState
    ($s.Current -eq $Run.Desktop.Current) -and ($s.All.Count -eq $Run.Desktop.All.Count)
}

# ------------------------------------------------------------------ real data must never change

function Get-RealDataFingerprint {
    <#
      Hashes the default Noto data folder in this process's view and in a shell-launched process's view -
      on this machine they can be different folders (package redirection). Read-only.
    #>
    param($Run)
    $mine = Join-Path $env:LOCALAPPDATA 'Noto'
    $here = if (Test-Path $mine) { (Get-ChildItem $mine -Recurse -File | Sort-Object FullName | ForEach-Object { "$($_.FullName.Substring($mine.Length))|$($_.Length)|$((Get-FileHash $_.FullName).Hash)" }) -join ';' } else { 'absent' }
    $out = Join-Path $Run.Root ("realdata-{0}.txt" -f [guid]::NewGuid().ToString('N').Substring(0, 6))
    $shell = (Start-ShortLived $Run -Script 'realdata-probe.ps1' -Out $out) -join ';'
    [pscustomobject]@{ HarnessView = $here; ShellView = $shell }
}

# ------------------------------------------------------------------ the run envelope

function Invoke-IsolatedRun {
    <#
      Runs $Body on a temporary virtual desktop, then - whatever happened - stops every process the run started,
      restores the original desktop and checks the real data folders are byte-for-byte unchanged.
    #>
    param([Parameter(Mandatory)] $Run, [Parameter(Mandatory)] [scriptblock] $Body, [Parameter(Mandatory)] [string] $Title)
    $before = Get-RealDataFingerprint $Run
    $clean = $false; $restored = $false
    try { Enter-TempDesktop $Run; $null = & $Body }
    catch { Add-Invalid $Run "aborted: $($_.Exception.Message) (line $($_.InvocationInfo.ScriptLineNumber))" }
    finally { $clean = Stop-RunProcesses $Run; $restored = Exit-TempDesktop $Run }
    Add-Result $Run 'hygiene: every process this run started has exited' $clean "$($Run.Started.Count) started"
    $d = Get-DesktopState
    Add-Result $Run 'hygiene: original virtual desktop current, temporary one removed' $restored "current=$($d.Current) count=$($d.All.Count)"
    $after = Get-RealDataFingerprint $Run
    $null = Stop-RunProcesses $Run
    Add-Result $Run 'hygiene: real data unchanged (harness view)' ($before.HarnessView -eq $after.HarnessView) $after.HarnessView
    Add-Result $Run 'hygiene: real data unchanged (shell view)' (($before.ShellView -eq $after.ShellView) -and [bool]$after.ShellView) $after.ShellView
    Complete-ValidationRun $Run $Title
}

Export-ModuleMember -Function * -Variable VK
