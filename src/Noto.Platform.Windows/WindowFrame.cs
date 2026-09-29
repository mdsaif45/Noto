using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Noto.Platform.Windows;

/// <summary>
/// Measures a window's frame inset from Windows (ADR-007 §4, §6).
/// </summary>
/// <remarks>
/// <para>
/// Takes a <see cref="WindowHandle"/>, never a raw handle, so the platform
/// boundary stays free of <c>IntPtr</c> in either direction. How the
/// application obtains one is documented on <see cref="WindowHandle"/>.
/// </para>
/// <para>
/// <b>Never cached.</b> Every call queries Windows afresh. The inset is in
/// physical pixels and differs per DPI (7/0/7/7 was measured at 96 DPI only),
/// so a value reused after a DPI change would misplace the window.
/// </para>
/// <para>
/// Both rectangles must come from the same DPI context:
/// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> is always physical pixels, while
/// <c>GetWindowRect</c> is virtualised for a DPI-unaware caller. Noto is
/// PerMonitorV2, so both are physical.
/// </para>
/// </remarks>
public static unsafe class WindowFrame
{
    /// <summary>The inset of the given window, measured now.</summary>
    /// <exception cref="Win32Exception">The window rectangle could not be read.</exception>
    /// <exception cref="COMException">DWM did not report the frame bounds.</exception>
    public static FrameInset MeasureInset(WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        nint hwnd = window.Hwnd;

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT outer))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT frame,
            sizeof(NativeMethods.RECT));

        Marshal.ThrowExceptionForHR(hr);

        return FrameInset.Between(outer.ToPixelRect(), frame.ToPixelRect());
    }

    /// <summary>The visible frame, as DWM draws it, read now.</summary>
    /// <exception cref="COMException">DWM did not report the frame bounds.</exception>
    internal static PixelRect VisibleFrame(nint hwnd)
    {
        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT frame,
            sizeof(NativeMethods.RECT));

        Marshal.ThrowExceptionForHR(hr);

        return frame.ToPixelRect();
    }
}
