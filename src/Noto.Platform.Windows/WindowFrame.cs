using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Noto.Platform.Windows;

/// <summary>
/// Measures a window's frame inset from Windows (ADR-007 §4, §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal, deliberately, until one boundary decision is made.</b> The
/// measurement needs the window's <c>HWND</c>. A WinUI window's handle is
/// obtained through <c>WinRT.Interop.WindowNative</c>, which lives in
/// <c>Microsoft.WinUI</c> — an assembly this project must never reference —
/// so the handle has to arrive from <c>Noto.Windows</c> as a parameter. The
/// platform boundary test forbids any public signature carrying a raw handle.
/// How a handle may legitimately cross that boundary is a contract question
/// for the #16 owner, not something to settle by quietly relaxing the test.
/// The measurement itself is implemented and runtime-verified meanwhile.
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
internal static unsafe class WindowFrame
{
    /// <summary>The inset of the given window, measured now.</summary>
    /// <exception cref="Win32Exception">The window rectangle could not be read.</exception>
    /// <exception cref="COMException">DWM did not report the frame bounds.</exception>
    internal static FrameInset MeasureInset(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT window))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT frame,
            sizeof(NativeMethods.RECT));

        Marshal.ThrowExceptionForHR(hr);

        return FrameInset.Between(window.ToPixelRect(), frame.ToPixelRect());
    }
}
