# Runs ELEVATED (started through UAC by Invoke-SingleInstanceElevated.ps1). Launches Noto elevated twice:
#   A  on a root with no owner     -> must refuse: message box (read, then dismissed with OK), exit 3, never owns
#   B  on a root a normal Noto owns -> must hand off: exit 0, no window
# Writes one line per fact to -Out, then 'done'. Never touches anything outside the two roots it is given.
param([Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $NoOwnerRoot, [Parameter(Mandatory)] [string] $OwnedRoot,
      [Parameter(Mandatory)] [string] $NoOwnerMutex, [Parameter(Mandatory)] [string] $Out)
$ErrorActionPreference = 'Stop'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("pid=$PID parent=$((Get-Process -Id $PID).Parent.Id) start=$((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)")
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class Elev
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr t, int c, out int v, int l, out int r);

    public static bool Elevated()
    {
        int v, r; GetTokenInformation(System.Security.Principal.WindowsIdentity.GetCurrent().Token, 20, out v, 4, out r); return v != 0;
    }

    static List<IntPtr> Windows(int pid, bool dialogs)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            int p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) { var c = new StringBuilder(64); GetClassName(h, c, 64); if ((c.ToString() == "#32770") == dialogs) list.Add(h); }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static string Text(IntPtr d)
    {
        var parts = new List<string>();
        EnumChildWindows(d, (h, l) => { var c = new StringBuilder(64); GetClassName(h, c, 64); if (c.ToString() == "Static") { var t = new StringBuilder(1024); GetWindowText(h, t, 1024); if (t.Length > 0) parts.Add(t.ToString()); } return true; }, IntPtr.Zero);
        return string.Join(" ", parts);
    }

    public static string Watch(Process p, int timeoutMs, string mutexToCheck)
    {
        int maxWindows = 0; long dialogAt = -1; string dialog = ""; bool ownedWhileOpen = false;
        var clock = Stopwatch.StartNew();
        while (!p.HasExited && clock.ElapsedMilliseconds < timeoutMs)
        {
            maxWindows = Math.Max(maxWindows, Windows(p.Id, false).Count);
            var d = Windows(p.Id, true);
            if (d.Count > 0)
            {
                if (dialogAt < 0)
                {
                    dialogAt = clock.ElapsedMilliseconds; dialog = Text(d[0]);
                    if (mutexToCheck != null) { Mutex m; if (Mutex.TryOpenExisting(@"Global\" + mutexToCheck, out m)) { ownedWhileOpen = true; m.Dispose(); } }
                }
                if (clock.ElapsedMilliseconds - dialogAt >= 200)
                {
                    PostMessage(d[0], 0x0111, new IntPtr(1), IntPtr.Zero);
                    PostMessage(d[0], 0x0010, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(300);
                }
            }
            Thread.Sleep(10);
        }
        bool exited = p.HasExited; if (exited) p.WaitForExit();
        return string.Format("exited={0} code={1} ms={2} windows={3} dialogMs={4} mutexWhileDialog={5} dialog={6}",
            exited, exited ? p.ExitCode.ToString() : "", clock.ElapsedMilliseconds, maxWindows, dialogAt, ownedWhileOpen, dialog);
    }
}
'@
try {
    $lines.Add("elevated=$([Elev]::Elevated())")
    $a = Start-Process -FilePath $Exe -ArgumentList "--data-root=`"$NoOwnerRoot`"" -PassThru
    $lines.Add("A pid=$($a.Id) $([Elev]::Watch($a, 30000, $NoOwnerMutex))")
    $m = $null; $lines.Add("A mutexAfter=$([System.Threading.Mutex]::TryOpenExisting("Global\$NoOwnerMutex", [ref]$m))")
    $b = Start-Process -FilePath $Exe -ArgumentList "--data-root=`"$OwnedRoot`"" -PassThru
    $lines.Add("B pid=$($b.Id) $([Elev]::Watch($b, 30000, $null))")
}
catch { $lines.Add("error=$($_.Exception.Message)") }
$lines.Add('done')
Set-Content -LiteralPath $Out -Value $lines
