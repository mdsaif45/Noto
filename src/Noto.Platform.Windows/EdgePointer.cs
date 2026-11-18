using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Noto.Core.Activation;
using Noto.Core.Workspace;

namespace Noto.Platform.Windows;

/// <summary>
/// Hot Side and drag to reveal (#185; parity A4, A15; ADR-007 §9): one low-level mouse hook, on a thread of its own,
/// that exists only while at least one of the two is on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a hook, and why its own thread.</b> A hook observes the pointer at the screen's edge without taking that
/// edge from anything: a strip window there would take the clicks of every maximized window's scroll bar. A low-level
/// hook is called through the message loop of the thread that installed it, and every pointer move on the system
/// waits for it; Windows removes, without telling, a hook that takes too long. So it lives on a thread that does
/// nothing else, never on the UI thread, which a long layout or a save can hold.
/// </para>
/// <para>
/// <b>The callback is minimal.</b> It updates the buttons held, asks <see cref="EdgeReveal"/> (numbers in, a step out),
/// starts or stops one timer, and returns the next hook's answer. It observes and never swallows. Nothing is
/// allocated per event, and nothing is traced there.
/// </para>
/// <para>
/// <b>Idle cost.</b> No hook and no thread while both settings are off. While one is on, the cost is per pointer
/// event; the thread waits in <c>GetMessage</c>, and the one timer runs only while a gesture is armed at the edge.
/// What a dwell needs to know (whether Noto is shown, the foreground application, the lock) is read once, when it
/// elapses, on this thread; a reveal is handed to the caller, which carries it to the UI thread.
/// </para>
/// </remarks>
public sealed unsafe class EdgePointer : IDisposable
{
    /// <summary>The corner at each end of the edge that never reveals, in device-independent pixels: a maximized window's caption buttons sit there.</summary>
    internal const int CornerDip = 48;

    // The installed one: the hook and timer callbacks are static, as unmanaged callbacks must be, and find it here.
    // Set on the UI thread before the hook thread starts and cleared after it has ended; one at a time.
    private static volatile EdgePointer? _installed;

    private readonly WindowHandle _workspace;
    private readonly Action<int> _reveal;

    // Hook thread only.
    private readonly EdgeReveal _machine = new();
    private PointerButtons _buttons;
    private nuint _timer;

    // Replaced whole from the UI thread, read once per event on the hook thread.
    private volatile Configuration? _configuration;

    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private int _error;

    private EdgePointer(WindowHandle workspace, Action<int> reveal)
    {
        _workspace = workspace;
        _reveal = reveal;
    }

    /// <summary>Whether an edge pointer is installed now.</summary>
    internal static bool IsInstalled => _installed is not null;

    /// <summary>
    /// Installs the hook on a new thread. Nothing reveals until <see cref="Configure"/> has said what is on.
    /// </summary>
    /// <param name="workspace">The workspace window: its display is the one whose edge is watched.</param>
    /// <param name="reveal">
    /// Called on the hook's thread, with the time it was decided on the <see cref="Environment.TickCount"/> clock,
    /// when a gesture reveals Noto. It must only hand the request to the UI thread.
    /// </param>
    /// <returns>The installed pointer, or <see langword="null"/> when Windows refused the hook.</returns>
    /// <exception cref="InvalidOperationException">One is already installed.</exception>
    public static EdgePointer? Start(WindowHandle workspace, Action<int> reveal)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(reveal);

        if (_installed is not null)
        {
            throw new InvalidOperationException("An edge pointer is already installed.");
        }

        var pointer = new EdgePointer(workspace, reveal);
        _installed = pointer;

        using (var ready = new ManualResetEventSlim())
        {
            pointer._thread = new Thread(() => pointer.Run(ready)) { IsBackground = true, Name = "Noto edge pointer" };
            pointer._thread.Start();
            ready.Wait();
        }

        if (pointer._hook == 0)
        {
            pointer._thread.Join();
            pointer._thread = null;
            Release(pointer);
            Trace.WriteLine($"Noto.Edge hook installed=False error={pointer._error}");
            return null;
        }

        Trace.WriteLine("Noto.Edge hook installed=True");
        return pointer;
    }

    /// <summary>
    /// What is on, and the edge as it is now: the display the workspace is on, its work area and the side. Read again
    /// whenever a setting or the side changes and whenever Noto is hidden, the state a reveal starts from.
    /// </summary>
    public void Configure(bool hotSide, bool drag, DockEdge side)
    {
        Configuration? next = null;

        try
        {
            DisplayMonitor display = DisplayMonitors.ForWindow(_workspace);
            EdgeZone zone = ZoneFor(display, DisplayMonitors.Enumerate(), side);
            int slopX = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG));
            int slopY = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDRAG));

            next = new Configuration(new EdgeRevealOptions(hotSide, drag, zone, slopX, slopY), display.Bounds);
            Trace.WriteLine($"Noto.Edge configured hotSide={hotSide} drag={drag} side={side} shared={zone.Shared}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // No edge known: nothing reveals until the next configuration, which every hide makes.
            Trace.WriteLine($"Noto.Edge configured=False error={ex.GetType().Name}");
        }

        _configuration = next;
    }

    /// <summary>Removes the hook and ends its thread, once.</summary>
    public void Dispose()
    {
        if (_thread is null)
        {
            return;
        }

        // The thread leaves its loop, stops the timer and unhooks, on itself.
        _ = NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        _ = _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        Release(this);

        Trace.WriteLine("Noto.Edge hook removed");
    }

    /// <summary>The buttons held after <paramref name="message"/>, from the ones held before it.</summary>
    internal static PointerButtons Track(uint message, PointerButtons held) => message switch
    {
        NativeMethods.WM_LBUTTONDOWN => held | PointerButtons.Left,
        NativeMethods.WM_LBUTTONUP => held & ~PointerButtons.Left,
        NativeMethods.WM_RBUTTONDOWN => held | PointerButtons.Right,
        NativeMethods.WM_RBUTTONUP => held & ~PointerButtons.Right,
        NativeMethods.WM_MBUTTONDOWN => held | PointerButtons.Middle,
        NativeMethods.WM_MBUTTONUP => held & ~PointerButtons.Middle,
        NativeMethods.WM_XBUTTONDOWN => held | PointerButtons.Extra,
        NativeMethods.WM_XBUTTONUP => held & ~PointerButtons.Extra,
        _ => held,
    };

    /// <summary>
    /// The docked edge of <paramref name="display"/>: its bounds' edge, where the pointer stops, live over its work
    /// area's height less a corner at each end, and shared when another display touches it.
    /// </summary>
    internal static EdgeZone ZoneFor(DisplayMonitor display, IReadOnlyList<DisplayMonitor> all, DockEdge side)
    {
        PixelRect bounds = display.Bounds;
        int corner = (int)Math.Round(CornerDip * (double)display.Dpi / 96.0);
        bool shared = all.Any(d => d.Bounds != bounds
            && (side == DockEdge.Right ? d.Bounds.Left == bounds.Right : d.Bounds.Right == bounds.Left)
            && d.Bounds.Top < bounds.Bottom && d.Bounds.Bottom > bounds.Top);

        return new EdgeZone(
            side == DockEdge.Left ? WorkspaceEdge.Left : WorkspaceEdge.Right,
            bounds.Left,
            bounds.Right,
            display.WorkArea.Top + corner,
            display.WorkArea.Bottom - corner,
            shared);
    }

    /// <summary>
    /// Whether a window is full-screen on <paramref name="display"/>: it covers the whole display, taskbar included,
    /// and has no caption. A maximized window under an auto-hiding taskbar covers it too, but keeps its caption.
    /// </summary>
    internal static bool FillsDisplay(PixelRect window, nint style, PixelRect display) =>
        window.Left <= display.Left && window.Top <= display.Top && window.Right >= display.Right && window.Bottom >= display.Bottom
        && (style & (nint)NativeMethods.WS_CAPTION) != (nint)NativeMethods.WS_CAPTION;

    private static void Release(EdgePointer pointer)
    {
        if (ReferenceEquals(_installed, pointer))
        {
            _installed = null;
        }
    }

    /// <summary>The foreground window is full-screen on the docked display. The desktop itself never is.</summary>
    internal static bool ForegroundFills(PixelRect display)
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        nint shell = NativeMethods.GetShellWindow();

        if (foreground == 0 || foreground == shell
            || (shell != 0 && NativeMethods.GetWindowThreadProcessId(foreground, out _) == NativeMethods.GetWindowThreadProcessId(shell, out _)))
        {
            return false;
        }

        return NativeMethods.GetWindowRect(foreground, out NativeMethods.RECT outer)
            && FillsDisplay(outer.ToPixelRect(), NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_STYLE), display);
    }

    /// <summary>The session is locked, or another desktop (a secure prompt) has the input: this one cannot be opened.</summary>
    internal static bool InputElsewhere()
    {
        nint desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);

        if (desktop == 0)
        {
            return true;
        }

        _ = NativeMethods.CloseDesktop(desktop);
        return false;
    }

    /// <summary>The foreground thread is moving or sizing a window: a window dragged to the edge to snap it is no drop.</summary>
    private static bool ForegroundMoving()
    {
        NativeMethods.GUITHREADINFO info = new() { cbSize = (uint)sizeof(NativeMethods.GUITHREADINFO) };
        return NativeMethods.GetGUIThreadInfo(0, &info) && (info.flags & NativeMethods.GUI_INMOVESIZE) != 0;
    }

    [UnmanagedCallersOnly]
    private static nint OnMouse(int code, nint message, nint data)
    {
        EdgePointer? pointer = _installed;

        if (code >= 0 && pointer is not null)
        {
            NativeMethods.MSLLHOOKSTRUCT* info = (NativeMethods.MSLLHOOKSTRUCT*)data;

            try
            {
                pointer.OnPointer((uint)message, info->x, info->y, unchecked((int)info->time));
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                // Never across the native boundary, where an exception ends the process (as the recorder's hook).
                Trace.WriteLine($"Noto.Edge pointer failed error={ex.GetType().Name}");
            }
        }

        return NativeMethods.CallNextHookEx(0, code, message, data);
    }

    [UnmanagedCallersOnly]
    private static void OnTimer(nint hwnd, uint message, nuint id, uint time)
    {
        EdgePointer? pointer = _installed;

        if (pointer is null)
        {
            return;
        }

        try
        {
            pointer.OnDue();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Trace.WriteLine($"Noto.Edge dwell failed error={ex.GetType().Name}");
        }
    }

    /// <summary>One pointer event, inside the hook: numbers in, at most one timer call out.</summary>
    private void OnPointer(uint message, int x, int y, int time)
    {
        _buttons = Track(message, _buttons);

        switch (_machine.Moved(x, y, _buttons, time, _configuration?.Options))
        {
            case EdgeStep.Arm:
                _timer = NativeMethods.SetTimer(0, _timer, (uint)_machine.Dwell, &OnTimer);
                break;

            case EdgeStep.Disarm:
                StopTimer();
                break;
        }
    }

    /// <summary>The armed gesture's dwell elapsed: read what may refuse it, once, and reveal or not.</summary>
    private void OnDue()
    {
        if (_machine.Armed == EdgeGesture.None)
        {
            StopTimer();
            return;
        }

        Configuration? configuration = _configuration;
        int now = Environment.TickCount;
        RevealContext context = configuration is null
            ? default
            : new RevealContext(
                Shown: WindowActivation.PresenceOf(_workspace) is WorkspacePresence.Background or WorkspacePresence.Foreground,
                Immersive: ForegroundFills(configuration.Display),
                Locked: InputElsewhere(),
                Moving: ForegroundMoving());

        EdgeDecision decision = _machine.Elapsed(now, configuration?.Options, context);

        // Still armed: the timer fired early, and fires again (a system timer repeats until stopped).
        if (_machine.Armed == EdgeGesture.None)
        {
            StopTimer();
        }

        if (decision.Outcome == EdgeOutcome.None)
        {
            return;
        }

        // The decision only, never a position: evidence for the runtime harness.
        Trace.WriteLine($"Noto.Edge gesture={decision.Gesture} outcome={decision.Outcome}");

        if (decision.Outcome == EdgeOutcome.Reveal)
        {
            _reveal(now);
        }
    }

    private void StopTimer()
    {
        if (_timer != 0)
        {
            _ = NativeMethods.KillTimer(0, _timer);
            _timer = 0;
        }
    }

    /// <summary>The hook's thread: install, pump until told to quit, then stop the timer and unhook on the same thread.</summary>
    private void Run(ManualResetEventSlim ready)
    {
        NativeMethods.MSG message = default;

        // The thread's message queue exists before anyone learns its id, so a quit posted at once is never lost.
        _ = NativeMethods.PeekMessage(&message, 0, 0, 0, NativeMethods.PM_NOREMOVE);
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, &OnMouse, NativeMethods.GetModuleHandle(null), 0);
        _error = _hook == 0 ? Marshal.GetLastPInvokeError() : 0;
        ready.Set();

        if (_hook == 0)
        {
            return;
        }

        while (NativeMethods.GetMessage(&message, 0, 0, 0) > 0)
        {
            _ = NativeMethods.DispatchMessage(&message);
        }

        StopTimer();
        _ = NativeMethods.UnhookWindowsHookEx(_hook);
    }

    /// <summary>What the hook reads per event, replaced whole: the options, and the docked display's bounds for the full-screen check.</summary>
    private sealed record Configuration(EdgeRevealOptions Options, PixelRect Display);
}
