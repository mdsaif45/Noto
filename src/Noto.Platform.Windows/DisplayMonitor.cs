using System.Runtime.InteropServices;

namespace Noto.Platform.Windows;

/// <summary>
/// Identifies a physical display across enumerations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opaque.</b> Callers compare it and store it — #16 composes it into the
/// per-monitor settings key <c>workspace.width::&lt;id&gt;</c> — and must not
/// parse it. What it contains is a Windows detail that can change.
/// </para>
/// <para>
/// It is deliberately <b>not</b> the GDI device name (<c>\\.\DISPLAY5</c>).
/// That is an ordinal Windows reassigns when displays are reconnected, so a
/// width remembered against it would silently move to a different monitor.
/// The identity is the monitor's device interface path instead; see
/// <see cref="DisplayMonitors"/> for how it is obtained and when it is not.
/// </para>
/// </remarks>
public readonly record struct MonitorId
{
    private MonitorId(string value) => Value = value;

    /// <summary>The identity as stored. Opaque; compare, never parse.</summary>
    public string Value { get; }

    /// <summary>Wraps a stored identity.</summary>
    /// <exception cref="ArgumentException">The value is blank.</exception>
    public static MonitorId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new MonitorId(value);
    }

    public override string ToString() => Value;
}

/// <summary>
/// One display, as Windows reports it at the moment of enumeration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounds and work area are both kept, and they are not interchangeable.</b>
/// The work area excludes the taskbar and any other reserved desktop space;
/// everything Noto positions is placed against it, never against the bounds
/// (ADR-007). The bounds are carried for diagnostics and for identifying which
/// monitor a point is on.
/// </para>
/// <para>
/// Coordinates are virtual-screen pixels, so <b>they may be negative</b>: a
/// display arranged to the left of, or above, the primary has a negative
/// origin. Nothing here assumes (0, 0).
/// </para>
/// <para>
/// A value type with no handle. The <c>HMONITOR</c> used to build it never
/// leaves this assembly.
/// </para>
/// </remarks>
public sealed record DisplayMonitor
{
    /// <summary>
    /// Describes one display from values already read from Windows.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="deviceName"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dpi"/> is zero, or <paramref name="workArea"/> is empty.
    /// </exception>
    public DisplayMonitor(
        MonitorId id,
        string deviceName,
        PixelRect bounds,
        PixelRect workArea,
        uint dpi,
        bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Width, nameof(workArea));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Height, nameof(workArea));

        Id = id;
        DeviceName = deviceName;
        Bounds = bounds;
        WorkArea = workArea;
        Dpi = dpi;
        IsPrimary = isPrimary;
    }

    public MonitorId Id { get; }

    /// <summary>The GDI device name, e.g. <c>\\.\DISPLAY1</c>. Not stable; see <see cref="MonitorId"/>.</summary>
    public string DeviceName { get; }

    /// <summary>The whole display, in physical pixels.</summary>
    public PixelRect Bounds { get; }

    /// <summary>The display minus the taskbar and other reserved space, in physical pixels.</summary>
    public PixelRect WorkArea { get; }

    /// <summary>Effective DPI; 96 is 100%.</summary>
    public uint Dpi { get; }

    public bool IsPrimary { get; }

    /// <summary>The work area as a placement at this display's scale.</summary>
    public WindowPlacement WorkAreaPlacement => new(DeviceName, WorkArea, Dpi);
}

/// <summary>
/// Enumerates the displays attached to the desktop.
/// </summary>
/// <remarks>
/// <para>
/// <b>A snapshot, not a subscription.</b> Topology changes whenever a display
/// is connected, disconnected, rearranged or rescaled; callers re-enumerate on
/// those events rather than holding the result. A display that disappears
/// while being enumerated is simply omitted.
/// </para>
/// <para>
/// <b>DPI awareness matters.</b> The rectangles and DPI returned are those
/// Windows reports to the calling process. Noto declares PerMonitorV2 in its
/// manifest, so it receives physical pixels and each display's true DPI. A
/// process without that declaration receives virtualised values on scaled
/// displays.
/// </para>
/// </remarks>
public static unsafe class DisplayMonitors
{
    /// <summary>All attached displays, in the order Windows enumerates them.</summary>
    /// <exception cref="InvalidOperationException">Windows refused the enumeration.</exception>
    public static IReadOnlyList<DisplayMonitor> Enumerate() => DescribeAll(MonitorHandles());

    /// <summary>The display a window is on, read now.</summary>
    /// <remarks>
    /// <para>
    /// The display holding the largest share of the window, as Windows decides
    /// it; a window on no display resolves to the nearest one. This is the
    /// "current monitor" a workspace docks against (#16).
    /// </para>
    /// <para>
    /// A fresh read, like <see cref="Enumerate"/>: the work area changes when
    /// the taskbar moves or auto-hides, so a stored result would go stale.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Windows returned no display, or the display could not be described.
    /// </exception>
    public static DisplayMonitor ForWindow(WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        nint handle = NativeMethods.MonitorFromWindow(window.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

        // A zero handle needs no separate check: it cannot be described.
        return TryDescribe(handle, out DisplayMonitor? monitor)
            ? monitor
            : throw new InvalidOperationException("Windows did not report a display for the window.");
    }

    /// <summary>The handle of every attached display, as Windows enumerates them.</summary>
    /// <exception cref="InvalidOperationException">Windows refused the enumeration.</exception>
    internal static IReadOnlyList<nint> MonitorHandles()
    {
        var handles = new List<nint>();
        GCHandle pin = GCHandle.Alloc(handles);

        try
        {
            if (!NativeMethods.EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(pin)))
            {
                throw new InvalidOperationException("Windows did not enumerate the display monitors.");
            }
        }
        finally
        {
            pin.Free();
        }

        return handles;
    }

    /// <summary>
    /// Describes each monitor handle, skipping any that can no longer be read.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Enumerate"/> only so the skip can be tested:
    /// enumeration yields handles to real displays, so the vanished-display
    /// path is otherwise unreachable from a test.
    /// </remarks>
    internal static IReadOnlyList<DisplayMonitor> DescribeAll(IReadOnlyList<nint> handles)
    {
        var monitors = new List<DisplayMonitor>(handles.Count);

        foreach (nint handle in handles)
        {
            // A display that vanished mid-enumeration cannot be described;
            // it is skipped, not reported.
            if (!TryDescribe(handle, out DisplayMonitor? monitor))
            {
                continue;
            }

            monitors.Add(monitor);
        }

        return monitors;
    }

    /// <summary>
    /// The enumeration callback. Records the handle and nothing else.
    /// </summary>
    /// <remarks>
    /// An exception escaping an <see cref="UnmanagedCallersOnlyAttribute"/>
    /// method terminates the process, so every query that can fail runs after
    /// enumeration returns, in managed code, instead of in here.
    /// </remarks>
    [UnmanagedCallersOnly]
    private static int Collect(nint hMonitor, nint hdc, NativeMethods.RECT* clip, nint data)
    {
        var handles = (List<nint>)GCHandle.FromIntPtr(data).Target!;
        handles.Add(hMonitor);
        return 1;
    }

    private static bool TryDescribe(nint handle, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DisplayMonitor? monitor)
    {
        monitor = null;

        var info = new NativeMethods.MONITORINFOEXW { cbSize = (uint)sizeof(NativeMethods.MONITORINFOEXW) };

        if (!NativeMethods.GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpi, out _) != 0 || dpi == 0)
        {
            return false;
        }

        string deviceName = new(info.szDevice);
        PixelRect work = info.rcWork.ToPixelRect();

        if (string.IsNullOrWhiteSpace(deviceName) || work.Width <= 0 || work.Height <= 0)
        {
            return false;
        }

        monitor = new DisplayMonitor(
            IdentityOf(deviceName),
            deviceName,
            info.rcMonitor.ToPixelRect(),
            work,
            dpi,
            (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0);

        return true;
    }

    /// <summary>
    /// The monitor's device interface path, or the device name when Windows
    /// does not provide one.
    /// </summary>
    /// <remarks>
    /// The interface path (<c>\\?\DISPLAY#…#{e6f07b5f-…}</c>) identifies the
    /// physical monitor on its connection and survives reconnection and
    /// renumbering. The fallback is weaker — the device name can move to a
    /// different monitor when displays are reattached — but a remembered width
    /// landing on the wrong display is recoverable, whereas refusing to
    /// identify a display at all would leave it unusable.
    /// </remarks>
    private static MonitorId IdentityOf(string deviceName)
    {
        var device = new NativeMethods.DISPLAY_DEVICEW { cb = (uint)sizeof(NativeMethods.DISPLAY_DEVICEW) };

        if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            string path = new(device.DeviceID);

            if (!string.IsNullOrWhiteSpace(path))
            {
                return MonitorId.From(path);
            }
        }

        return MonitorId.From(deviceName);
    }
}
