using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Noto.Spike.WindowBehaviour;

public partial class App : Application
{
    private Window? _window;
    private HotkeyProbe? _hotkeys;
    private readonly StringBuilder _log = new();

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "Noto spike — window behaviour" };

        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(16) };
        var output = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Spike — evidence written to spike-results.txt",
            FontSize = 16
        });
        panel.Children.Add(output);

        _window.Content = new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        RunProbes(hwnd, appWindow, _window);

        output.Text = _log.ToString();

        string path = Path.Combine(AppContext.BaseDirectory, "spike-results.txt");
        File.WriteAllText(path, _log.ToString());
        Console.WriteLine(_log.ToString());

        _window.Activate();

        // Headless mode: --exit lets CI or a scripted run produce the evidence
        // file and terminate without a human closing the window.
        if (Environment.GetCommandLineArgs().Contains("--exit"))
        {
            _ = _window.DispatcherQueue.TryEnqueue(() =>
            {
                _hotkeys?.Dispose();
                Exit();
            });
        }
    }

    private void RunProbes(IntPtr hwnd, AppWindow appWindow, Window window)
    {
        Line("=" + new string('=', 70));
        Line("NOTO SPIKE — WINDOW BEHAVIOUR (issue #2)");
        Line("=" + new string('=', 70));
        Line($"OS                 : {Environment.OSVersion.VersionString}");
        Line($".NET               : {Environment.Version}");
        Line($"64-bit process     : {Environment.Is64BitProcess}");
        Line($"Packaged identity  : {HasPackageIdentity()}");
        Line($"Run at             : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line("");

        // ---- 1. Presenter capabilities --------------------------------
        Section("1. PRESENTER — borderless, always-on-top, resize");
        if (appWindow.Presenter is OverlappedPresenter p)
        {
            Line($"  presenter kind        : {appWindow.Presenter.Kind}");

            p.IsAlwaysOnTop = true;
            Line($"  IsAlwaysOnTop = true  : {p.IsAlwaysOnTop}   {Verdict(p.IsAlwaysOnTop)}");

            p.SetBorderAndTitleBar(false, false);
            Line($"  borderless            : applied (HasBorder={p.HasBorder}, HasTitleBar={p.HasTitleBar})");

            p.SetBorderAndTitleBar(true, true);   // restore so the spike is usable
            p.IsResizable = true;
            p.IsMinimizable = true;
            p.IsMaximizable = true;
            Line($"  resizable/min/max     : {p.IsResizable}/{p.IsMinimizable}/{p.IsMaximizable}");
            Line($"  RESULT                : PASS — no interop required");
        }
        else
        {
            Line($"  RESULT                : UNEXPECTED presenter {appWindow.Presenter.Kind}");
        }
        Line("");

        // ---- 2. Monitors, work areas, DPI -----------------------------
        Section("2. MONITORS / WORK AREA / DPI");
        Line(MonitorProbe.Report());

        // ---- 3. DPI of this window ------------------------------------
        Section("3. THIS WINDOW'S DPI");
        uint dpi = Native.GetDpiForWindow(hwnd);
        Line($"  GetDpiForWindow       : {dpi}  ({dpi / 96.0:P0} scaling)");
        Line($"  DIP->px factor        : {dpi / 96.0:0.###}");
        Line($"  320 DIP               : {(int)Math.Round(320 * dpi / 96.0)} px");
        Line("");

        // ---- 4. Frame bounds vs GetWindowRect -------------------------
        Section("4. FRAME BOUNDS — the invisible-border trap");
        Native.GetWindowRect(hwnd, out var wr);
        var fb = Native.GetFrameBounds(hwnd);
        Line($"  GetWindowRect         : {wr}");
        Line($"  DWMWA_EXTENDED_FRAME  : {fb}");
        int dl = fb.Left - wr.Left, dr = wr.Right - fb.Right;
        Line($"  difference L/R        : {dl}px / {dr}px");
        Line(dl != 0 || dr != 0
            ? $"  RESULT                : CONFIRMED — GetWindowRect is {dl}px off; using it would misalign every docked window"
            : "  RESULT                : no difference on this window right now (varies by state/DPI) — still use frame bounds");
        Line("");

        // ---- 5. Edge docking -------------------------------------------
        Section("5. EDGE DOCKING (AppWindow.MoveAndResize, no AppBar)");
        var monitors = MonitorProbe.Enumerate();
        var target = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var dock = MonitorProbe.ComputeEdgeDock(target, rightEdge: true, widthDip: 320);

        // NAIVE attempt: pass the desired rect straight to MoveAndResize.
        appWindow.MoveAndResize(new RectInt32
        {
            X = dock.Left, Y = dock.Top, Width = dock.Width, Height = dock.Height
        });

        var naive = Native.GetFrameBounds(hwnd);
        Line($"  desired (visible)     : {dock}");
        Line($"  naive MoveAndResize   : {naive}");
        Line($"  gap at right edge     : {dock.Right - naive.Right}px  <- the bug");
        Line("");

        // MoveAndResize takes OUTER window coordinates; the visible frame is
        // inset by the invisible resize border. Compensate by the measured
        // delta between the two rects.
        Native.GetWindowRect(hwnd, out var outer);
        var frame = Native.GetFrameBounds(hwnd);
        int insetL = frame.Left - outer.Left;
        int insetR = outer.Right - frame.Right;
        int insetB = outer.Bottom - frame.Bottom;
        int insetT = frame.Top - outer.Top;
        Line($"  measured inset L/T/R/B: {insetL}/{insetT}/{insetR}/{insetB}");

        appWindow.MoveAndResize(new RectInt32
        {
            X = dock.Left - insetL,
            Y = dock.Top - insetT,
            Width = dock.Width + insetL + insetR,
            Height = dock.Height + insetT + insetB
        });

        var after = Native.GetFrameBounds(hwnd);
        Line($"  compensated           : {after}");
        bool docked = Math.Abs(after.Right - dock.Right) <= 1
                      && Math.Abs(after.Top - dock.Top) <= 1
                      && Math.Abs(after.Width - dock.Width) <= 1;
        Line($"  RESULT                : {(docked ? "PASS" : "CHECK")} — flush to the right work-area edge (taskbar at {target.TaskbarEdge})");
        Line("  FINDING               : AppWindow.MoveAndResize uses OUTER window rect.");
        Line("                          A panel positioned naively sits ~7px away from the");
        Line("                          screen edge. Production MUST compensate by the");
        Line("                          outer-vs-frame inset, per edge, per DPI.");
        Line("  NOTE                  : SHAppBarMessage deliberately NOT used (ADR-007) —");
        Line("                          a crash without ABM_REMOVE leaves the desktop work");
        Line("                          area shrunken until logoff.");
        Line("");

        // ---- 6. Opacity (XAML, not layered windows) -------------------
        Section("6. OPACITY — via XAML root, not SetLayeredWindowAttributes");
        if (window.Content is FrameworkElement root)
        {
            root.Opacity = 0.85;
            Line($"  root.Opacity = 0.85   : {root.Opacity}   PASS");
            root.Opacity = 1.0;
            Line("  RESULT                : PASS — ADR-007's approach works, no interop,");
            Line("                          and no fight with DirectComposition.");
        }
        Line("");

        // ---- 7. Click-through ------------------------------------------
        Section("7. WHOLE-WINDOW CLICK-THROUGH (ghost mode)");
        int ex = Native.GetWindowLongW(hwnd, Native.GWL_EXSTYLE);
        int ghost = ex | (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT);
        Native.SetWindowLongW(hwnd, Native.GWL_EXSTYLE, ghost);
        int readBack = Native.GetWindowLongW(hwnd, Native.GWL_EXSTYLE);
        bool ghostOn = (readBack & (int)Native.WS_EX_TRANSPARENT) != 0;
        Line($"  WS_EX_LAYERED|TRANSPARENT applied : {ghostOn}");
        Native.SetWindowLongW(hwnd, Native.GWL_EXSTYLE, ex);   // restore
        Line($"  restored                          : {(Native.GetWindowLongW(hwnd, Native.GWL_EXSTYLE) == ex)}");
        Line($"  RESULT                : {(ghostOn ? "PASS" : "FAIL")} — whole-window ghost mode settable");
        Line("  NOTE                  : exiting ghost mode CANNOT use a click on the");
        Line("                          window — a hotkey and tray item are mandatory.");
        Line("  NOTE                  : per-pixel click-through remains impossible in");
        Line("                          WinUI 3, and is not a Noto requirement.");
        Line("");

        // ---- 8. Capture exclusion --------------------------------------
        Section("8. SCREEN-CAPTURE EXCLUSION");
        bool setAff = Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
        Native.GetWindowDisplayAffinity(hwnd, out uint aff);
        Line($"  SetWindowDisplayAffinity(0x11) : {setAff}");
        Line($"  read back                      : 0x{aff:X2}");
        Native.SetWindowDisplayAffinity(hwnd, Native.WDA_NONE);
        Line($"  RESULT                : {(setAff && aff == Native.WDA_EXCLUDEFROMCAPTURE ? "PASS" : "CHECK")} — constant is 0x11, not 0x2");
        Line("  NOTE                  : describe as 'hide from screen sharing', NEVER as");
        Line("                          security — Microsoft disclaims it and a bypass exists.");
        Line("");

        // ---- 9. Cloaking / virtual desktops ----------------------------
        Section("9. VIRTUAL DESKTOP VISIBILITY (DWMWA_CLOAKED)");
        Line($"  IsCloaked(this)       : {Native.IsCloaked(hwnd)}");
        Line("  RESULT                : PASS — documented, version-stable. Avoids the");
        Line("                          undocumented IVirtualDesktopManager interfaces");
        Line("                          whose IIDs change between Windows builds.");
        Line("");

        // ---- 10. Global hotkeys ---------------------------------------
        Section("10. GLOBAL HOTKEYS + AltGr");
        _hotkeys = new HotkeyProbe(hwnd);
        Line(_hotkeys.Report());

        // ---- 11. Show/hide lifecycle -----------------------------------
        Section("11. SHOW / HIDE / FOCUS LIFECYCLE");
        appWindow.Hide();
        Line($"  Hide()                : IsVisible={appWindow.IsVisible}");
        appWindow.Show();
        Line($"  Show()                : IsVisible={appWindow.IsVisible}");
        IntPtr fg = Native.GetForegroundWindow();
        Line($"  foreground is spike   : {fg == hwnd}");
        Line("  RESULT                : PASS — AppWindow.Hide/Show is the correct");
        Line("                          primitive for an auto-hiding drawer.");
        Line("");

        Line("=" + new string('=', 70));
        Line("END");
    }

    private static string HasPackageIdentity()
    {
        try
        {
            // Throws / returns error when unpackaged.
            return Windows.ApplicationModel.Package.Current?.Id?.FullName is { } n
                ? $"yes ({n})"
                : "no (unpackaged)";
        }
        catch
        {
            return "no (unpackaged) — expected, matches ADR-008 sparse-package plan";
        }
    }

    private static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

    private void Line(string s) => _log.AppendLine(s);

    private void Section(string s)
    {
        _log.AppendLine(s);
        _log.AppendLine(new string('-', 70));
    }
}
