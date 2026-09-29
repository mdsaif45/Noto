using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Noto.Platform.Windows;

/// <summary>
/// Where to put a real window so it docks to an edge of the display it is on
/// (#16 slice 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Computes; does not move.</b> The move itself is
/// <c>AppWindow.MoveAndResize</c> (ADR-007 §4), a Windows App SDK call that
/// belongs to <c>Noto.Windows</c> and must never be referenced from here. This
/// class is the platform half: it reads the window's current display and its
/// frame inset, and hands back outer coordinates ready for that call.
/// </para>
/// <para>
/// Everything is read at the moment of the call — the display, its work area,
/// its DPI, and the inset — and nothing is kept. The inset is measured on the
/// window's current display, so it is correct for a move within that display;
/// moving a window to a display at a different scale changes the inset, and
/// that case belongs to #30.
/// </para>
/// </remarks>
public static class WindowDocking
{
    /// <summary>
    /// The outer bounds that dock <paramref name="window"/> to
    /// <paramref name="edge"/> of the display it is on now.
    /// </summary>
    /// <param name="window">
    /// A shown window. The inset comes from DWM's frame bounds, which describe
    /// what is drawn; measuring a window before it is shown is not relied on.
    /// </param>
    /// <param name="edge">Which vertical edge.</param>
    /// <param name="requestedWidthDip">The width asked for, in DIPs; clamped with <see cref="WorkspaceWidth.Clamp"/>.</param>
    /// <exception cref="InvalidOperationException">Windows reported no display for the window.</exception>
    /// <exception cref="Win32Exception">The window rectangle could not be read.</exception>
    /// <exception cref="COMException">DWM did not report the frame bounds.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edge"/> is not a defined edge.</exception>
    public static PixelRect OuterBoundsFor(WindowHandle window, DockEdge edge, double requestedWidthDip)
    {
        DisplayMonitor monitor = DisplayMonitors.ForWindow(window);
        FrameInset inset = WindowFrame.MeasureInset(window);

        return DockGeometry.OuterBounds(monitor, edge, requestedWidthDip, inset);
    }
}
