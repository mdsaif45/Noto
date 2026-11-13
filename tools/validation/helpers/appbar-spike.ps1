# #153 spike (Windows PowerShell or pwsh): a window docked as an application desktop toolbar (SHAppBarMessage) on the
# right edge, reserving -Width pixels, which then ends as -End says:
#   remove     ABM_REMOVE, then exit (the clean path)
#   kill       waits to be killed from outside (Stop-Process -Force: a crash Noto cannot catch)
#   failfast   Environment.FailFast after docking (a crash, as Windows sees one)
#   exit       exits without ABM_REMOVE (an update or a bug that skips the clean path)
# Writes "docked <left>,<top>,<right>,<bottom>" (its rectangle) to -Status once docked.
param([Parameter(Mandatory)] [string] $Status, [ValidateSet('remove', 'kill', 'failfast', 'exit')] [string] $End = 'kill', [int] $Width = 300)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Windows.Forms;
public class SpikeBar : Form {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABE_RIGHT = 2;
    public int BarWidth = 300; public RECT Docked;
    public SpikeBar() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Text = "Noto dock spike"; BackColor = System.Drawing.Color.DarkSlateBlue; }
    public void Reserve() {
        var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = Handle; d.uCallbackMessage = 0x8001;
        SHAppBarMessage(ABM_NEW, ref d);
        var s = Screen.PrimaryScreen.Bounds;
        d.uEdge = ABE_RIGHT; d.rc.L = s.Right - BarWidth; d.rc.T = s.Top; d.rc.R = s.Right; d.rc.B = s.Bottom;
        SHAppBarMessage(ABM_QUERYPOS, ref d);
        d.rc.L = d.rc.R - BarWidth;
        SHAppBarMessage(ABM_SETPOS, ref d);
        Docked = d.rc;
        Bounds = new System.Drawing.Rectangle(d.rc.L, d.rc.T, d.rc.R - d.rc.L, d.rc.B - d.rc.T);
    }
    public void Undock() { var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = Handle; SHAppBarMessage(ABM_REMOVE, ref d); }
}
'@
$bar = New-Object SpikeBar
$bar.BarWidth = $Width
$bar.add_Shown({
        $bar.Reserve()
        Set-Content -LiteralPath $Status -Value ("docked {0},{1},{2},{3}" -f $bar.Docked.L, $bar.Docked.T, $bar.Docked.R, $bar.Docked.B)
        $timer = New-Object System.Windows.Forms.Timer; $timer.Interval = 3000
        $timer.add_Tick({
                $timer.Stop()
                switch ($End) {
                    'remove' { $bar.Undock(); [Environment]::Exit(0) }
                    'failfast' { [Environment]::FailFast('Noto dock spike: a crash while docked') }
                    'exit' { [Environment]::Exit(0) }
                }
            })
        if ($End -ne 'kill') { $timer.Start() }
    })
[System.Windows.Forms.Application]::Run($bar)
