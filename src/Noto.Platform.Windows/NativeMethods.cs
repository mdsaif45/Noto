using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Noto.Platform.Windows;

/// <summary>
/// The Win32 calls the platform primitives need, and no others.
/// </summary>
/// <remarks>
/// <para>
/// Internal by design: every handle and native struct stays inside this
/// assembly, and callers receive <see cref="PixelRect"/>,
/// <see cref="FrameInset"/>, <see cref="DisplayMonitor"/> and
/// <see cref="WindowPlacement"/> instead.
/// </para>
/// <para>
/// Every struct here is blittable — fixed <c>char</c> buffers rather than
/// <c>ByValTStr</c> strings — so every import can be a
/// <see cref="LibraryImportAttribute"/> with no runtime marshalling. The
/// window-behaviour spike needed <c>DllImport</c> for
/// <c>MONITORINFOEXW</c> because it declared the device name as a string;
/// a fixed buffer removes that need.
/// </para>
/// </remarks>
internal static unsafe partial class NativeMethods
{
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    /// <summary><c>MDT_EFFECTIVE_DPI</c> — the DPI Windows scales UI by.</summary>
    internal const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>
    /// Asks <c>EnumDisplayDevicesW</c> for the monitor's device interface
    /// path rather than its display-adapter description.
    /// </summary>
    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // Messages the docked-window subclass handles (#16 slice 3).
    internal const uint WM_WINDOWPOSCHANGING = 0x0046;
    internal const uint WM_NCDESTROY = 0x0082;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_SIZING = 0x0214;
    internal const uint WM_ENTERSIZEMOVE = 0x0231;
    internal const uint WM_EXITSIZEMOVE = 0x0232;

    // WM_NCHITTEST results.
    internal const int HTCLIENT = 1;
    internal const int HTLEFT = 10;
    internal const int HTRIGHT = 11;
    internal const int HTTOP = 12;
    internal const int HTTOPLEFT = 13;
    internal const int HTTOPRIGHT = 14;
    internal const int HTBOTTOM = 15;
    internal const int HTBOTTOMLEFT = 16;
    internal const int HTBOTTOMRIGHT = 17;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;

    /// <summary><c>SW_RESTORE</c>: activates and restores a minimized window to its normal placement.</summary>
    internal const int SW_RESTORE = 9;

    // Global hotkey (#16 slice 4).
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    /// <summary>The parent that makes a window message-only: never shown, never enumerated.</summary>
    internal const nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAY_DEVICEW
    {
        public uint cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        delegate* unmanaged<nint, nint, RECT*, nint, int> lpfnEnum,
        nint dwData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEXW lpmi);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayDevices(
        string lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICEW lpDisplayDevice,
        uint dwFlags);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    // Workspace show/hide (#16 slice 5).

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    /// <remarks>
    /// <c>SetLastError</c> for the same reason as the test imports: a plain
    /// signature makes the generator emit a direct <c>extern</c>, which CodeQL
    /// reports as <c>cs/call-to-unmanaged-code</c>. Harmless here.
    /// </remarks>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetForegroundWindow();

    /// <summary>
    /// When the message being handled was posted, in milliseconds since
    /// the system started — the clock <see cref="Environment.TickCount"/>
    /// reads. Valid only while that message is being handled.
    /// </summary>
    /// <remarks><c>SetLastError</c> only to avoid a direct extern, as above.</remarks>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetMessageTime();

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    // Window subclassing, comctl32. Measured under this application's
    // manifest: comctl32 5.82 is loaded and exports all three, so no
    // Common-Controls v6 dependency is needed.

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(
        nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass,
        nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(
        nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass);

    /// <summary>
    /// The next window procedure in the subclass chain.
    /// </summary>
    /// <remarks>
    /// <b>Called from exactly one place</b>, <c>WindowSubclass.CallDefault</c>,
    /// which every subclass in this assembly shares.
    /// Its signature is all plain types, so the generator emits a direct
    /// <c>extern</c> and CodeQL reports <c>cs/call-to-unmanaged-code</c> at
    /// that call. It is required: a subclass must pass every message it does
    /// not consume down the chain, and there is no managed equivalent for
    /// hit-testing, sizing or position interception (#16 slice 3 D5 spike).
    /// </remarks>
    [LibraryImport("comctl32.dll")]
    internal static partial nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}
