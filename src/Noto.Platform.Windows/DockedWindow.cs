using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noto.Platform.Windows;

/// <summary>
/// A docked window's width, as the user left it after resizing.
/// </summary>
public sealed class DockResizedEventArgs(MonitorId monitor, double visibleWidthDip) : EventArgs
{
    /// <summary>The display the window is docked on.</summary>
    public MonitorId Monitor { get; } = monitor;

    /// <summary>
    /// The width of the <b>visible</b> frame, in DIPs — never the outer
    /// width, which includes the invisible resize border and would grow the
    /// window by that border every time it was restored.
    /// </summary>
    public double VisibleWidthDip { get; } = visibleWidthDip;
}

/// <summary>
/// Keeps a docked window docked while the user resizes it (#16 slice 3).
/// </summary>
/// <remarks>
/// <para>
/// A native window subclass, chosen by the D5 spike over the managed
/// alternative: <c>OverlappedPresenter</c>'s preferred sizes cannot restrict
/// which edges resize, cannot refuse a snap or a move, give no drag-end
/// signal, and bound the outer size in units this machine cannot determine.
/// Four messages do the work:
/// </para>
/// <list type="table">
///   <item><term><c>WM_NCHITTEST</c></term><description>only the inner edge is a resize edge; the docked edge, top and bottom behave as client area</description></item>
///   <item><term><c>WM_SIZING</c></term><description>every drag step is refitted: docked edge fixed, full work-area height, width clamped by <see cref="WorkspaceWidth.Clamp"/></description></item>
///   <item><term><c>WM_WINDOWPOSCHANGING</c></term><description>any move or resize Noto did not make itself — a snap, <c>Win+Arrow</c>, another process — is rewritten back to the dock</description></item>
///   <item><term><c>WM_EXITSIZEMOVE</c></term><description>the drag has ended: <see cref="Resized"/> reports the visible width, once</description></item>
/// </list>
/// <para>
/// <b>Noto's own moves go through <see cref="MoveOwn"/>.</b> The position
/// guard lets through only what happens inside it, which is how the
/// application's <c>AppWindow.MoveAndResize</c> still docks the window while
/// every foreign reposition is refused.
/// </para>
/// <para>
/// The window's chrome is the application's to set (it owns the presenter):
/// the dock expects no title bar and no minimise or maximise, because a
/// caption drag in WinUI 3 does not pass through this window's hit test and
/// could not be refused here.
/// </para>
/// <para>
/// <b>Lifetime.</b> Installed on the thread that owns the window and removed
/// when the window is destroyed (<c>WM_NCDESTROY</c>); there is nothing to
/// dispose. The edge is fixed for the life of the window — the setting is
/// read at launch (#16 slice 3).
/// </para>
/// </remarks>
public sealed unsafe class DockedWindow
{
    private const nuint SubclassId = 1;

    private readonly WindowHandle _window;
    private readonly nint _hwnd;
    private GCHandle _self;
    private bool _ownMove;
    private bool _inSizeLoop;
    private int _sizingSteps;
    private double? _rememberedWidthDip;
    private FrameInset? _placedInset;

    private DockedWindow(WindowHandle window, DockEdge edge)
    {
        _window = window;
        _hwnd = window.Hwnd;
        Edge = edge;
    }

    /// <summary>
    /// Raised once when the user finishes resizing, with the width they left.
    /// </summary>
    /// <remarks>
    /// Not raised for a drag that changed nothing, nor for any move Noto makes
    /// itself. Raised on the window's thread, from inside the window
    /// procedure: a handler that throws is contained and does not reach
    /// Windows.
    /// </remarks>
    public event EventHandler<DockResizedEventArgs>? Resized;

    /// <summary>The edge the window is kept docked to.</summary>
    public DockEdge Edge { get; }

    /// <summary>Whether the subclass is still installed. Cleared on <c>WM_NCDESTROY</c>.</summary>
    internal bool IsAttached { get; private set; }

    /// <summary>
    /// The width, in DIPs, the window is docked at whenever it is put back on
    /// the dock — <see langword="null"/> until it is known.
    /// </summary>
    /// <remarks>
    /// The <b>requested</b> width, not the effective one: re-docking clamps it
    /// to the work area of the display it lands on, and that clamp never
    /// writes back here. Set only by <see cref="Remember"/>, by a finished
    /// user resize, and — when neither has happened — once from the window's
    /// own docked geometry after the first <see cref="MoveOwn"/>. Never from
    /// a refused move, a minimized frame or anything off-screen.
    /// </remarks>
    internal double? RememberedWidthDip => _rememberedWidthDip;

    /// <summary>
    /// Records the width the window was asked to dock at, before it is
    /// docked. The application passes the width it resolved from settings.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requestedWidthDip"/> is not a positive, finite width.</exception>
    public void Remember(double requestedWidthDip)
    {
        if (!double.IsFinite(requestedWidthDip) || requestedWidthDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedWidthDip), requestedWidthDip, "Not a positive, finite width.");
        }

        _rememberedWidthDip = requestedWidthDip;
    }

    /// <summary>
    /// Installs the dock on a window.
    /// </summary>
    /// <remarks>
    /// Call before the first dock, on the window's own thread, so that first
    /// <c>MoveAndResize</c> already runs through <see cref="MoveOwn"/>.
    /// </remarks>
    /// <exception cref="Win32Exception">Windows refused the subclass.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edge"/> is not a defined edge.</exception>
    public static DockedWindow Attach(WindowHandle window, DockEdge edge)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (edge is not (DockEdge.Left or DockEdge.Right))
        {
            throw new ArgumentOutOfRangeException(nameof(edge), edge, "Not a dock edge.");
        }

        var docked = new DockedWindow(window, edge);
        docked._self = GCHandle.Alloc(docked);

        if (!NativeMethods.SetWindowSubclass(docked._hwnd, &Proc, SubclassId, (nuint)GCHandle.ToIntPtr(docked._self)))
        {
            int error = Marshal.GetLastPInvokeError();
            docked._self.Free();
            throw new Win32Exception(error);
        }

        docked.IsAttached = true;
        docked.CapturePlacedInset();
        return docked;
    }

    /// <summary>
    /// Runs one of Noto's own repositionings past the position guard.
    /// </summary>
    /// <remarks>
    /// Everything the window does inside <paramref name="move"/> is allowed
    /// through unchanged; nothing outside it is. Not re-entrant, and not
    /// needed to be: moves happen on the window's thread.
    /// </remarks>
    public void MoveOwn(Action move)
    {
        ArgumentNullException.ThrowIfNull(move);

        _ownMove = true;

        try
        {
            move();
        }
        finally
        {
            _ownMove = false;
        }

        CapturePlacedInset();

        if (_rememberedWidthDip is null)
        {
            RememberDockedGeometry();
        }
    }

    /// <summary>
    /// Records the frame inset while the window is placed on a display.
    /// </summary>
    /// <remarks>
    /// A restore is docked while the window is still minimized, and a
    /// minimized window's frame inset is not its docked inset — measured: a
    /// plain overlapped window reports no bottom inset while minimized. So the
    /// restore uses the inset last measured while the window was placed;
    /// every other reposition measures it fresh (ADR-007: never a constant).
    /// </remarks>
    private void CapturePlacedInset()
    {
        if (NativeMethods.IsIconic(_hwnd) || !NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT outer)
            || !IsOnAnyDisplay(outer.ToPixelRect(), DisplayMonitors.Enumerate()))
        {
            return;
        }

        try
        {
            _placedInset = WindowFrame.MeasureInset(_window);
        }
        catch (Exception ex) when (ex is Win32Exception or COMException)
        {
            // Unmeasurable now: keep the last known inset.
        }
    }

    /// <summary>
    /// The first known-good width, taken from the window's own docked
    /// geometry when nothing has told the dock what width to keep.
    /// </summary>
    /// <remarks>Skipped while the window is minimized, off every display, or unmeasurable.</remarks>
    private void RememberDockedGeometry()
    {
        if (NativeMethods.IsIconic(_hwnd) || !NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT outer))
        {
            return;
        }

        IReadOnlyList<DisplayMonitor> displays = DisplayMonitors.Enumerate();

        if (!IsOnAnyDisplay(outer.ToPixelRect(), displays))
        {
            return;
        }

        try
        {
            PixelRect visible = WindowFrame.VisibleFrame(_hwnd);

            if (visible.Width > 0 && DisplayMonitors.Nearest(outer.ToPixelRect(), displays) is DisplayMonitor display)
            {
                _rememberedWidthDip = display.WorkAreaPlacement.PixelsToDip(visible.Width);
            }
        }
        catch (COMException)
        {
            // DWM did not report the frame: leave the width unknown rather than guess.
        }
    }

    /// <summary>
    /// What a hit test on a docked window resolves to.
    /// </summary>
    /// <remarks>
    /// Only the inner edge stays a resize edge — the left edge of a
    /// right-docked window, the right edge of a left-docked one — including
    /// its two corners, which become that edge alone. The docked edge, the
    /// top and the bottom become client area. Every other result is kept.
    /// </remarks>
    internal static int MapHitTest(DockEdge edge, int hit)
    {
        bool right = edge == DockEdge.Right;

        return hit switch
        {
            NativeMethods.HTLEFT or NativeMethods.HTTOPLEFT or NativeMethods.HTBOTTOMLEFT =>
                right ? NativeMethods.HTLEFT : NativeMethods.HTCLIENT,
            NativeMethods.HTRIGHT or NativeMethods.HTTOPRIGHT or NativeMethods.HTBOTTOMRIGHT =>
                right ? NativeMethods.HTCLIENT : NativeMethods.HTRIGHT,
            NativeMethods.HTTOP or NativeMethods.HTBOTTOM => NativeMethods.HTCLIENT,
            _ => hit,
        };
    }

    /// <summary>
    /// Ends a drag: reports the visible width, if the drag resized anything.
    /// </summary>
    /// <remarks>Separate from the window procedure so the report can be tested directly.</remarks>
    internal void OnExitSizeMove()
    {
        _inSizeLoop = false;

        if (_sizingSteps == 0)
        {
            return;
        }

        _sizingSteps = 0;

        DisplayMonitor display = DisplayMonitors.ForWindow(_window);
        PixelRect visible = WindowFrame.VisibleFrame(_hwnd);
        double widthDip = display.WorkAreaPlacement.PixelsToDip(visible.Width);

        // The user chose this width: it is what the dock now keeps.
        _rememberedWidthDip = widthDip;
        CapturePlacedInset();

        Resized?.Invoke(this, new DockResizedEventArgs(display.Id, widthDip));
    }

    /// <summary>Begins a drag; also the testable half of <c>WM_ENTERSIZEMOVE</c>.</summary>
    internal void OnEnterSizeMove()
    {
        _inSizeLoop = true;
        _sizingSteps = 0;
    }

    /// <summary>Refits one drag step. The testable half of <c>WM_SIZING</c>.</summary>
    internal PixelRect OnSizing(PixelRect proposedOuter)
    {
        _sizingSteps++;

        return DockGeometry.ResizedOuterBounds(
            DisplayMonitors.ForWindow(_window), Edge, proposedOuter, WindowFrame.MeasureInset(_window));
    }

    /// <summary>
    /// Where a proposed reposition should land instead, or <see langword="null"/> to let it through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Let through: Noto's own moves, the steps of a user resize (already
    /// refitted by <c>WM_SIZING</c>), changes that neither move nor size, and
    /// anything while the window is minimized — minimizing is Windows' to
    /// place. Everything else is sent back to the dock (<see cref="GuardTarget"/>).
    /// </para>
    /// <para>
    /// A restore from minimized is one of the "everything else": Windows
    /// proposes the saved normal placement while the window still sits at its
    /// off-screen minimized position, and it is re-docked like any other
    /// reposition. No transition state is kept, so duplicate, interrupted or
    /// failed restores leave nothing behind.
    /// </para>
    /// </remarks>
    internal PixelRect? OnPositionChanging(uint flags, PixelRect proposedOuter)
    {
        if (LetsThrough(_ownMove, _inSizeLoop, flags, NativeMethods.IsIconic(_hwnd)))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT outer))
        {
            return null;
        }

        int visibleWidth = 0;

        try
        {
            visibleWidth = WindowFrame.VisibleFrame(_hwnd).Width;
        }
        catch (COMException)
        {
            // No frame reported: only a remembered width can be used.
        }

        IReadOnlyList<DisplayMonitor> displays = DisplayMonitors.Enumerate();
        FrameInset? inset;

        if (IsOnAnyDisplay(outer.ToPixelRect(), displays))
        {
            inset = WindowFrame.MeasureInset(_window);
            _placedInset = inset;
        }
        else
        {
            // Being restored from the parking position: its inset there is not the docked one.
            inset = _placedInset;
        }

        return GuardTarget(displays, Edge, outer.ToPixelRect(), proposedOuter, visibleWidth, _rememberedWidthDip, inset);
    }

    /// <summary>Whether a proposed reposition passes the guard unchanged.</summary>
    internal static bool LetsThrough(bool ownMove, bool inSizeLoop, uint flags, bool minimized)
    {
        const uint neither = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;

        return ownMove || inSizeLoop || minimized || (flags & neither) == neither;
    }

    /// <summary>
    /// The docked outer bounds a reposition is rewritten to, or
    /// <see langword="null"/> when there is nothing trustworthy to dock by.
    /// </summary>
    /// <param name="displays">The connected displays.</param>
    /// <param name="edge">The edge the window is docked to.</param>
    /// <param name="currentOuter">Where the window is now.</param>
    /// <param name="proposedOuter">Where the reposition would put it.</param>
    /// <param name="currentVisibleWidthPx">The current visible width, or 0 when unknown.</param>
    /// <param name="rememberedWidthDip">The width the dock keeps (<see cref="RememberedWidthDip"/>).</param>
    /// <param name="inset">
    /// The window's frame inset while placed — measured now, or for a restore
    /// the last placed measurement; <see langword="null"/> when unknown.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The display.</b> The one under the window when the window is on a
    /// display — an ordinary move is judged by where the window is. When it
    /// is on none, it is at its minimized parking position and being
    /// restored, so the display is the one under the <b>proposed</b>
    /// rectangle — the placement Windows saved before minimizing. Choosing by
    /// the parking position instead picks whichever display is nearest to
    /// it, which is how the docked window used to restore off-screen.
    /// </para>
    /// <para>
    /// <b>The width.</b> The remembered width, clamped to that display's work
    /// area by <see cref="DockGeometry.OuterBounds"/>. Failing that, the
    /// current visible width — but only when the window is on a display, so
    /// a minimized frame is never measured as a dock width. With neither, or
    /// with no known inset, the reposition is let through.
    /// </para>
    /// </remarks>
    internal static PixelRect? GuardTarget(
        IReadOnlyList<DisplayMonitor> displays,
        DockEdge edge,
        PixelRect currentOuter,
        PixelRect proposedOuter,
        int currentVisibleWidthPx,
        double? rememberedWidthDip,
        FrameInset? inset)
    {
        ArgumentNullException.ThrowIfNull(displays);

        if (inset is not FrameInset measuredInset)
        {
            return null;
        }

        bool currentIsPlaced = IsOnAnyDisplay(currentOuter, displays);
        PixelRect basis;

        if (currentIsPlaced)
        {
            basis = currentOuter;
        }
        else if (IsOnAnyDisplay(proposedOuter, displays))
        {
            basis = proposedOuter;
        }
        else
        {
            return null;
        }

        if (DisplayMonitors.Nearest(basis, displays) is not DisplayMonitor display)
        {
            return null;
        }

        double? widthDip = rememberedWidthDip
            ?? (currentIsPlaced && currentVisibleWidthPx > 0 ? display.WorkAreaPlacement.PixelsToDip(currentVisibleWidthPx) : null);

        return widthDip is double width ? DockGeometry.OuterBounds(display, edge, width, measuredInset) : null;
    }

    /// <summary>Whether a rectangle overlaps any display's bounds.</summary>
    internal static bool IsOnAnyDisplay(PixelRect rect, IReadOnlyList<DisplayMonitor> displays)
    {
        foreach (DisplayMonitor d in displays)
        {
            if (Math.Min(rect.Right, d.Bounds.Right) > Math.Max(rect.Left, d.Bounds.Left)
                && Math.Min(rect.Bottom, d.Bounds.Bottom) > Math.Max(rect.Top, d.Bounds.Top))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The rectangle a <c>WINDOWPOS</c> proposes, filling what it leaves unchanged from where the window is.</summary>
    private static PixelRect Proposed(nint hwnd, NativeMethods.WINDOWPOS* pos)
    {
        _ = NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT current);
        bool keepPosition = (pos->flags & NativeMethods.SWP_NOMOVE) != 0;
        bool keepSize = (pos->flags & NativeMethods.SWP_NOSIZE) != 0;
        int left = keepPosition ? current.Left : pos->x;
        int top = keepPosition ? current.Top : pos->y;
        int width = keepSize ? current.Right - current.Left : pos->cx;
        int height = keepSize ? current.Bottom - current.Top : pos->cy;

        return new PixelRect(left, top, left + width, top + height);
    }

    private void OnDestroy()
    {
        _ = NativeMethods.RemoveWindowSubclass(_hwnd, &Proc, SubclassId);
        IsAttached = false;
        _self.Free();
    }

    /// <summary>
    /// The window procedure.
    /// </summary>
    /// <remarks>
    /// An exception escaping an <see cref="UnmanagedCallersOnlyAttribute"/>
    /// method terminates the process, so every handler runs inside a catch.
    /// A handler that fails lets the message through unchanged: an unrefitted
    /// drag step or an unrefused move is recoverable, a crashed notes app is
    /// not.
    /// </remarks>
    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (GCHandle.FromIntPtr((nint)refData).Target is not DockedWindow self)
        {
            return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
        }

        try
        {
            switch (msg)
            {
                case NativeMethods.WM_NCHITTEST:
                    return MapHitTest(self.Edge, (int)WindowSubclass.CallDefault(hwnd, msg, wParam, lParam));

                case NativeMethods.WM_ENTERSIZEMOVE:
                    self.OnEnterSizeMove();
                    break;

                case NativeMethods.WM_SIZING:
                    {
                        var rect = (NativeMethods.RECT*)lParam;
                        PixelRect fitted = self.OnSizing(rect->ToPixelRect());
                        rect->Left = fitted.Left;
                        rect->Top = fitted.Top;
                        rect->Right = fitted.Right;
                        rect->Bottom = fitted.Bottom;
                        return 1;
                    }

                case NativeMethods.WM_WINDOWPOSCHANGING:
                    {
                        var pos = (NativeMethods.WINDOWPOS*)lParam;

                        if (self.OnPositionChanging(pos->flags, Proposed(hwnd, pos)) is PixelRect docked)
                        {
                            pos->x = docked.Left;
                            pos->y = docked.Top;
                            pos->cx = docked.Width;
                            pos->cy = docked.Height;
                            pos->flags &= ~(NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                        }

                        break;
                    }

                case NativeMethods.WM_EXITSIZEMOVE:
                    self.OnExitSizeMove();
                    break;

                case NativeMethods.WM_NCDESTROY:
                    self.OnDestroy();
                    break;

                default:
                    break;
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            // Wide on purpose: the Resized handler is arbitrary caller code,
            // and nothing may unwind into Windows. Not unconditional — a
            // process out of memory or stack is not a docking failure.
            Debug.WriteLine($"Docked window: message 0x{msg:X4} failed: {ex.GetType().Name}: {ex.Message}");
        }

        return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
    }
}
