using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Noto.Core.Activation;

namespace Noto.Platform.Windows;

/// <summary>
/// The Open Bar (#185 part 2; parity A3, J1, J27; ADR-007 §10): a small, half see-through handle at the docked edge
/// that shows Noto when clicked and imports the files dropped on it. It exists only while the setting is on.
/// </summary>
/// <remarks>
/// <para>
/// <b>A window, but not a strip.</b> §9 rejected an edge window for <i>hover</i>: one along the whole edge takes the
/// scroll bar of every maximized window under it. The bar is a visible handle the user aims at, 6 DIP wide and 96 DIP
/// tall, centred on the work area's height, so the rest of the edge stays the application's.
/// </para>
/// <para>
/// <b>Never in the way.</b> Topmost, a tool window (no taskbar button, no Alt+Tab entry), and it never activates: a
/// click on it leaves the foreground where it was until Noto itself is brought forward. Hidden while Noto is shown,
/// while a full-screen application is in front of the docked display and while the session is locked.
/// </para>
/// <para>
/// <b>Idle cost.</b> Messages only: its own (a click, a drop, the pointer over it, a display or colour change) and one
/// out-of-context foreground event, delivered through the UI thread's loop. The only timer is the one-shot fade of
/// <see cref="OpenBarMode.HideWhenIdle"/> (<see cref="OpenBar"/>).
/// </para>
/// <para>
/// <b>Thread.</b> Create, configure and dispose it on the UI thread; its callback is raised there, from inside the
/// window procedure, and a handler that throws is contained.
/// </para>
/// </remarks>
public sealed unsafe class OpenBarWindow : IDisposable
{
    /// <summary>The window's title: how a test or the runtime harness finds it.</summary>
    public const string Title = "Noto Open Bar";

    /// <summary>The bar's width, in device-independent pixels.</summary>
    internal const int WidthDip = 6;

    /// <summary>The bar's height, in device-independent pixels; never more than the work area's.</summary>
    internal const int HeightDip = 96;

    /// <summary>How opaque the drawn bar is: half see-through, as SideNotes' (A3). High contrast draws it opaque.</summary>
    internal const byte ShownAlpha = 0x99;

    /// <summary>How opaque the faded bar is: invisible, yet not zero, so it still takes the pointer, clicks and drops.</summary>
    internal const byte FadedAlpha = 1;

    private const nuint SubclassId = 4;
    private const nuint FadeTimer = 1;

    // The one bar: the foreground event's callback is static, as unmanaged callbacks must be, and finds it here.
    private static OpenBarWindow? _current;

    private readonly nint _hwnd;
    private readonly WindowHandle _workspace;
    private readonly Action<int, IReadOnlyList<string>> _open;
    private readonly OpenBar _machine = new();
    private GCHandle _self;
    private nint _foreground;
    private bool _disposed;
    private OpenBarMode _mode;
    private bool _drops;
    private DockEdge _side;
    private bool _notoHidden;
    private PixelRect _display;
    private OpenBarLook _look;

    private OpenBarWindow(nint hwnd, WindowHandle workspace, Action<int, IReadOnlyList<string>> open)
    {
        _hwnd = hwnd;
        _workspace = workspace;
        _open = open;
    }

    /// <summary>Whether a bar exists now.</summary>
    internal static bool IsCreated => _current is not null;

    /// <summary>The bar's window, for this assembly and its tests.</summary>
    internal nint Hwnd => _hwnd;

    /// <summary>
    /// Creates the bar on the calling thread, hidden. Nothing shows until <see cref="Configure"/> says what is on.
    /// </summary>
    /// <param name="workspace">The workspace window: the bar sits at the docked edge of its display.</param>
    /// <param name="open">
    /// Called on a click (no files) or a drop (the files), with when it was received on the
    /// <see cref="Environment.TickCount"/> clock. The application shows Noto, as a launch, and imports the files.
    /// </param>
    /// <exception cref="Win32Exception">Windows refused the window or its subclass.</exception>
    /// <exception cref="InvalidOperationException">A bar already exists.</exception>
    public static OpenBarWindow Create(WindowHandle workspace, Action<int, IReadOnlyList<string>> open)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(open);

        if (_current is not null)
        {
            throw new InvalidOperationException("An Open Bar already exists.");
        }

        nint hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_LAYERED,
            "STATIC",
            Title,
            NativeMethods.WS_POPUP,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);

        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var bar = new OpenBarWindow(hwnd, workspace, open);
        bar._self = GCHandle.Alloc(bar);

        if (!NativeMethods.SetWindowSubclass(hwnd, &Proc, SubclassId, (nuint)GCHandle.ToIntPtr(bar._self)))
        {
            int error = Marshal.GetLastPInvokeError();
            bar._self.Free();
            _ = NativeMethods.DestroyWindow(hwnd);
            throw new Win32Exception(error);
        }

        // A full-screen application coming to the front hides the bar; out of context, so it arrives as a message here.
        bar._foreground = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            0,
            &OnForeground,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _current = bar;
        Trace.WriteLine("Noto.OpenBar created");
        return bar;
    }

    /// <summary>
    /// What is on, the side, and whether Noto is hidden: the bar is placed at the docked edge of the workspace's
    /// display as it is now, and shown, faded or hidden. Called whenever any of these changes, and on every show and hide.
    /// </summary>
    public void Configure(OpenBarMode mode, bool drops, DockEdge side, bool notoHidden)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _mode = mode;
        _drops = drops;
        _side = side;
        _notoHidden = notoHidden;
        NativeMethods.DragAcceptFiles(_hwnd, drops);
        Place();
        Refresh();
        Trace.WriteLine($"Noto.OpenBar configured mode={mode} drops={drops} side={side} notoHidden={notoHidden}");
    }

    /// <summary>Destroys the window and stops following the foreground. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_foreground != 0)
        {
            _ = NativeMethods.UnhookWinEvent(_foreground);
        }

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        _ = NativeMethods.KillTimer(_hwnd, FadeTimer);

        // WM_NCDESTROY, handled below, removes the subclass and frees the handle.
        _ = NativeMethods.DestroyWindow(_hwnd);
        Trace.WriteLine("Noto.OpenBar removed");
    }

    /// <summary>
    /// Where the bar goes on a display: flush with the work area's docked edge (where Noto docks), centred on its
    /// height, <see cref="WidthDip"/> by <see cref="HeightDip"/> at the display's scale.
    /// </summary>
    internal static PixelRect Placement(PixelRect workArea, uint dpi, DockEdge side)
    {
        int width = Math.Max(1, (int)Math.Round(WidthDip * (double)dpi / 96.0));
        int height = Math.Min(workArea.Height, (int)Math.Round(HeightDip * (double)dpi / 96.0));
        int left = side == DockEdge.Right ? workArea.Right - width : workArea.Left;
        int top = workArea.Top + ((workArea.Height - height) / 2);
        return new PixelRect(left, top, left + width, top + height);
    }

    /// <summary>The bar's colour as a <c>COLORREF</c>: Highlight in high contrast (ADR-011), else the system's accent.</summary>
    private static uint Colour() =>
        HighContrast() || NativeMethods.DwmGetColorizationColor(out uint argb, out _) != 0
            ? NativeMethods.GetSysColor(NativeMethods.COLOR_HIGHLIGHT)
            : ((argb >> 16) & 0xFF) | (argb & 0xFF00) | ((argb & 0xFF) << 16);

    private static bool HighContrast()
    {
        NativeMethods.HIGHCONTRASTW contrast = new() { cbSize = (uint)sizeof(NativeMethods.HIGHCONTRASTW) };
        return NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST, contrast.cbSize, &contrast, 0)
            && (contrast.dwFlags & NativeMethods.HCF_HIGHCONTRASTON) != 0;
    }

    /// <summary>The files of a shell drop, read and released.</summary>
    private static List<string> DroppedFiles(nint drop)
    {
        var files = new List<string>();

        try
        {
            uint count = NativeMethods.DragQueryFile(drop, uint.MaxValue, null, 0);

            for (uint i = 0; i < count; i++)
            {
                uint length = NativeMethods.DragQueryFile(drop, i, null, 0);
                char[] buffer = new char[length + 1];

                fixed (char* text = buffer)
                {
                    if (NativeMethods.DragQueryFile(drop, i, text, (uint)buffer.Length) == length)
                    {
                        files.Add(new string(buffer, 0, (int)length));
                    }
                }
            }
        }
        finally
        {
            NativeMethods.DragFinish(drop);
        }

        return files;
    }

    [UnmanagedCallersOnly]
    private static void OnForeground(nint hook, uint message, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            _current?.Refresh();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Trace.WriteLine($"Noto.OpenBar foreground failed error={ex.GetType().Name}");
        }
    }

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (GCHandle.FromIntPtr((nint)refData).Target is not OpenBarWindow self)
        {
            return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
        }

        try
        {
            switch (msg)
            {
                case NativeMethods.WM_NCHITTEST:
                    // A static control lets the pointer through; the bar takes it.
                    return NativeMethods.HTCLIENT;

                case NativeMethods.WM_MOUSEACTIVATE:
                    return NativeMethods.MA_NOACTIVATE;

                case NativeMethods.WM_MOUSEMOVE:
                    self.Apply(self._machine.PointerMoved());
                    return 0;

                case NativeMethods.WM_LBUTTONUP:
                    self.Open([]);
                    return 0;

                case NativeMethods.WM_DROPFILES:
                    List<string> files = DroppedFiles(wParam);

                    if (self._drops && files.Count > 0)
                    {
                        self.Open(files);
                    }

                    return 0;

                case NativeMethods.WM_TIMER when (nuint)wParam == FadeTimer:
                    self.Apply(self._machine.Elapsed());
                    return 0;

                case NativeMethods.WM_DISPLAYCHANGE:
                case NativeMethods.WM_DPICHANGED:
                case NativeMethods.WM_SETTINGCHANGE:
                case NativeMethods.WM_SYSCOLORCHANGE:
                case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                    // The work area, the scale, high contrast or the accent may have changed: placed and drawn again.
                    self.Place();
                    self.Draw();
                    break;

                case NativeMethods.WM_ERASEBKGND:
                    return 1;

                case NativeMethods.WM_PAINT:
                    self.Paint();
                    return 0;

                case NativeMethods.WM_CLOSE:
                    // Only Dispose ends this window, as the tray's.
                    return 0;

                case NativeMethods.WM_NCDESTROY:
                    _ = NativeMethods.RemoveWindowSubclass(hwnd, &Proc, SubclassId);
                    self._self.Free();
                    break;
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            // The handler is arbitrary caller code, and nothing may unwind into Windows.
            Trace.WriteLine($"Noto.OpenBar message failed msg=0x{msg:X4} error={ex.GetType().Name}");
        }

        return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
    }

    private void Open(List<string> files)
    {
        // Local receive time, as the tray's: handled as it arrives, on the UI thread.
        Trace.WriteLine($"Noto.OpenBar open files={files.Count}");
        _open(Environment.TickCount, files);
    }

    /// <summary>
    /// Whether the bar may show now, read again: Noto hidden, no full-screen application in front of the docked display
    /// (§9's check) and the session not locked. On every configure and every foreground change.
    /// </summary>
    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        bool away = !_notoHidden || (_mode != OpenBarMode.Off && (EdgePointer.InputElsewhere() || EdgePointer.ForegroundFills(_display)));
        Apply(_machine.Set(_mode, away));
    }

    /// <summary>Carries out the machine's timer step, then shows the look it now has.</summary>
    private void Apply(EdgeStep step)
    {
        switch (step)
        {
            case EdgeStep.Arm:
                _ = NativeMethods.SetTimer(_hwnd, FadeTimer, OpenBar.IdleDelay, null);
                break;

            case EdgeStep.Disarm:
                _ = NativeMethods.KillTimer(_hwnd, FadeTimer);
                break;
        }

        Show(_machine.Look);
    }

    private void Show(OpenBarLook look)
    {
        if (look == _look)
        {
            return;
        }

        _look = look;
        Draw();

        // The look only, never a position: evidence for the runtime harness.
        Trace.WriteLine($"Noto.OpenBar look={look}");
    }

    /// <summary>Hidden, or drawn at its opacity (opaque in high contrast) and shown without activating.</summary>
    private void Draw()
    {
        if (_look == OpenBarLook.Hidden)
        {
            _ = NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            return;
        }

        byte alpha = _look == OpenBarLook.Faded ? FadedAlpha : HighContrast() ? (byte)0xFF : ShownAlpha;
        _ = NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
        _ = NativeMethods.InvalidateRect(_hwnd, 0, false);
        _ = NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    /// <summary>At the docked edge of the workspace's display, as it is now, topmost.</summary>
    private void Place()
    {
        try
        {
            DisplayMonitor display = DisplayMonitors.ForWindow(_workspace);
            PixelRect at = Placement(display.WorkArea, display.Dpi, _side);
            _display = display.Bounds;
            _ = NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, at.Left, at.Top, at.Width, at.Height, NativeMethods.SWP_NOACTIVATE);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // No display known: left where it was until the next configure.
            Trace.WriteLine($"Noto.OpenBar placed=False error={ex.GetType().Name}");
        }
    }

    private void Paint()
    {
        NativeMethods.PAINTSTRUCT paint;
        nint dc = NativeMethods.BeginPaint(_hwnd, &paint);

        if (dc == 0)
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(Colour());
        _ = NativeMethods.FillRect(dc, &paint.rcPaint, brush);
        _ = NativeMethods.DeleteObject(brush);
        _ = NativeMethods.EndPaint(_hwnd, &paint);
    }
}
