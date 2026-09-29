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

        Resized?.Invoke(this, new DockResizedEventArgs(display.Id, display.WorkAreaPlacement.PixelsToDip(visible.Width)));
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
    /// Let through: Noto's own moves, the steps of a user resize (already
    /// refitted by <c>WM_SIZING</c>), and changes that neither move nor size.
    /// Everything else is sent back to the dock at the current visible width.
    /// </remarks>
    internal PixelRect? OnPositionChanging(uint flags)
    {
        const uint neither = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;

        if (_ownMove || _inSizeLoop || (flags & neither) == neither)
        {
            return null;
        }

        DisplayMonitor display = DisplayMonitors.ForWindow(_window);
        PixelRect visible = WindowFrame.VisibleFrame(_hwnd);

        return DockGeometry.OuterBounds(
            display, Edge, display.WorkAreaPlacement.PixelsToDip(visible.Width), WindowFrame.MeasureInset(_window));
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

                        if (self.OnPositionChanging(pos->flags) is PixelRect docked)
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
