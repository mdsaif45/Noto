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

    /// <summary>
    /// Asks <c>MonitorFromWindow</c> for the nearest display when the window
    /// intersects none, so a window placed off-screen still resolves to one.
    /// </summary>
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
