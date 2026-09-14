using System.Runtime.InteropServices;

// CA5392 — pin native library resolution to System32 so a DLL of the same name
// dropped beside the executable cannot be loaded instead (DLL planting).
// Assembly-level because every P/Invoke here targets a system library.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Noto.Spike.WindowBehaviour;

/// <summary>
/// SPIKE — the Win32 surface Noto's window model needs.
///
/// Every entry here is a question issue #2 must answer. Anything that turns out
/// to be unnecessary should not survive into production.
/// </summary>
internal static partial class Native
{
    // ---- Window styles -------------------------------------------------

    internal const int GWL_EXSTYLE = -20;

    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;  // keeps it out of Alt+Tab

    // ---- Display affinity (capture exclusion) --------------------------
    // 0x11, NOT 0x2 — the value is frequently mis-transcribed. Win10 2004+.

    internal const uint WDA_NONE = 0x00;
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    // ---- Hotkey modifiers ----------------------------------------------

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    // ---- DWM -----------------------------------------------------------
    // DWMWA_EXTENDED_FRAME_BOUNDS is what must be used for alignment maths.
    // GetWindowRect includes ~7-8px of invisible resize border and will
    // silently misalign every docked window.

    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // MDT_EFFECTIVE_DPI
    internal const int MDT_EFFECTIVE_DPI = 0;

    // ---- P/Invoke ------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    // MONITORINFOEXW contains a fixed-size inline string (ByValTStr), which the
    // LibraryImport source generator cannot marshal (SYSLIB1051). DllImport's
    // runtime marshaller handles it. A real finding for the production interop
    // layer: LibraryImport is not a blanket replacement for DllImport.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    // ---- Helpers -------------------------------------------------------

    /// <summary>
    /// The frame bounds Noto must align against. <c>GetWindowRect</c> includes
    /// an invisible resize border; this does not.
    /// </summary>
    internal static RECT GetFrameBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT frame, Marshal.SizeOf<RECT>()) == 0)
        {
            return frame;
        }

        // Fall back rather than throw; the caller records that it happened.
        GetWindowRect(hwnd, out RECT fallback);
        return fallback;
    }

    internal static bool IsCloaked(IntPtr hwnd)
    {
        // Documented, version-stable, and answers "is this on the current
        // virtual desktop?" without touching the undocumented
        // IVirtualDesktopManager interfaces whose IIDs change per build.
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
               && cloaked != 0;
    }
}
