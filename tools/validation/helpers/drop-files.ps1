# Windows PowerShell (STA): drags files with OLE from ($args[0], $args[1]) and drops them at ($args[2], $args[3]).
# The drag starts on a small window of this script's own, shown there without taking the foreground (WS_EX_NOACTIVATE),
# so the foreground window stays what it was: the button is pressed on that window, the drag moves in steps to the drop
# point and the button is released there. Prints the effect the target answered.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System; using System.Drawing; using System.Runtime.InteropServices; using System.Threading; using System.Windows.Forms;
public class DragSource : Form {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    public DataObject Data; public Point From, To; public string Effect = "none";
    public DragSource() { FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Size = new Size(24, 24); BackColor = Color.Gray; ShowInTaskbar = false; }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000008 | 0x00000080; return p; } } // NOACTIVATE, TOPMOST, TOOLWINDOW
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Effect = DoDragDrop(Data, DragDropEffects.Copy).ToString(); Close(); }
    public void Run() {
        Location = new Point(From.X - 12, From.Y - 12);
        Shown += (s, e) => new Thread(() => {
            Thread.Sleep(300); SetCursorPos(From.X, From.Y); Thread.Sleep(100);
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); Thread.Sleep(300);                       // LEFTDOWN on this window
            for (int i = 1; i <= 20; i++) { SetCursorPos(From.X + (To.X - From.X) * i / 20, From.Y + (To.Y - From.Y) * i / 20); mouse_event(0x0001, 0, 0, 0, IntPtr.Zero); Thread.Sleep(40); }
            Thread.Sleep(400); mouse_event(0x0001, 0, 0, 0, IntPtr.Zero); Thread.Sleep(200);
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);                                             // LEFTUP at the drop point
        }) { IsBackground = true }.Start();
        Application.Run(this);
    }
}
'@
$files = New-Object System.Collections.Specialized.StringCollection
foreach ($p in $args[4..($args.Count - 1)]) { [void]$files.Add($p) }
$source = New-Object DragSource
$source.Data = New-Object System.Windows.Forms.DataObject
$source.Data.SetFileDropList($files)
$source.From = New-Object System.Drawing.Point ([int]$args[0]), ([int]$args[1])
$source.To = New-Object System.Drawing.Point ([int]$args[2]), ([int]$args[3])
$source.Run()
"effect=" + $source.Effect
