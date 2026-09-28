using System.Runtime.InteropServices;
using Noto.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Noto.Windows.Tests;

/// <summary>
/// The platform primitives against the real Windows desktop (#16 slice 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Runtime, not simulated.</b> These call the actual Win32 and DWM APIs on
/// whatever displays this machine has. They assert what must hold on
/// <i>any</i> topology — one display or many, any origin — and write the
/// measured layout to the test output as evidence, rather than asserting a
/// particular arrangement exists.
/// </para>
/// <para>
/// <b>What they cannot show.</b> Mixed-DPI behaviour and runtime scale
/// changes (#30) need displays at different scale factors; nothing here
/// substitutes for that.
/// </para>
/// <para>
/// Each test is tagged <see cref="DesktopSession.RequiresDesktopTrait"/> and
/// returns early without asserting when there is no interactive session. A
/// headless agent should exclude them with
/// <c>--filter "Category!=RequiresDesktop"</c> rather than read an early
/// return as a pass.
/// </para>
/// </remarks>
public sealed partial class PlatformRuntimeTests(ITestOutputHelper output)
{
    // ---------------------------------------------------- enumeration

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Enumerates_every_display_with_a_work_area_inside_its_bounds()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        IReadOnlyList<DisplayMonitor> displays = DisplayMonitors.Enumerate();

        foreach (DisplayMonitor d in displays)
        {
            output.WriteLine(
                $"{d.DeviceName}  bounds {d.Bounds}  work {d.WorkArea}  {d.Dpi} DPI  primary={d.IsPrimary}  id={d.Id}");
        }

        Assert.NotEmpty(displays);
        Assert.Single(displays, d => d.IsPrimary);

        foreach (DisplayMonitor d in displays)
        {
            Assert.True(d.Dpi > 0);
            Assert.InRange(d.WorkArea.Left, d.Bounds.Left, d.Bounds.Right);
            Assert.InRange(d.WorkArea.Right, d.Bounds.Left, d.Bounds.Right);
            Assert.InRange(d.WorkArea.Top, d.Bounds.Top, d.Bounds.Bottom);
            Assert.InRange(d.WorkArea.Bottom, d.Bounds.Top, d.Bounds.Bottom);
        }
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Monitor_identities_are_distinct_and_stable_across_enumerations()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        MonitorId[] first = [.. DisplayMonitors.Enumerate().Select(d => d.Id)];
        MonitorId[] second = [.. DisplayMonitors.Enumerate().Select(d => d.Id)];

        Assert.Equal(first.Length, first.Distinct().Count());
        Assert.Equal(first.OrderBy(i => i.Value), second.OrderBy(i => i.Value));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_workspace_docked_on_each_real_display_stays_inside_that_display()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        foreach (DisplayMonitor d in DisplayMonitors.Enumerate())
        {
            foreach (DockEdge edge in new[] { DockEdge.Left, DockEdge.Right })
            {
                PixelRect placed = DockGeometry.VisibleBounds(d, edge, 360).Bounds;
                output.WriteLine($"{d.DeviceName} {edge,-5} -> {placed}");

                Assert.Equal(d.WorkArea.Top, placed.Top);
                Assert.Equal(d.WorkArea.Bottom, placed.Bottom);
                Assert.InRange(placed.Left, d.WorkArea.Left, d.WorkArea.Right);
                Assert.InRange(placed.Right, d.WorkArea.Left, d.WorkArea.Right);
            }
        }
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_primary_work_area_matches_the_one_windows_reports_independently()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // SystemParametersInfo(SPI_GETWORKAREA) is a second, independent Win32
        // source for the primary display's work area. Agreement proves the
        // work area was taken from rcWork, not rcMonitor — without assuming
        // this machine's taskbar is visible, where it is, or how tall it is.
        DisplayMonitor primary = DisplayMonitors.Enumerate().Single(d => d.IsPrimary);
        PixelRect reported = Independent.PrimaryWorkArea();

        output.WriteLine($"primary work area {primary.WorkArea}; SPI_GETWORKAREA {reported}");

        Assert.Equal(reported, primary.WorkArea);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_display_is_identified_by_its_device_interface_path_when_windows_has_one()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // The GDI device name is an ordinal Windows reassigns on reconnection;
        // the interface path is not. Queried independently here, so a change
        // that fell back to the device name everywhere would fail.
        foreach (DisplayMonitor d in DisplayMonitors.Enumerate())
        {
            string? path = Independent.DeviceInterfacePath(d.DeviceName);
            output.WriteLine($"{d.DeviceName}: interface path {(path ?? "<none>")}");

            Assert.Equal(path ?? d.DeviceName, d.Id.Value);
        }
    }

    // ----------------------------------------------- frame inset (DWM)

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Measures_a_real_windows_frame_inset_through_dwm()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = TestWindow.Create(TestWindow.Overlapped);

        FrameInset inset = WindowFrame.MeasureInset(window.Handle);
        output.WriteLine($"overlapped window inset: {inset}");

        // An overlapped window has an invisible resize border on at least the
        // sides, and no edge of a real frame measures negative.
        Assert.True(inset.Left >= 0 && inset.Top >= 0 && inset.Right >= 0 && inset.Bottom >= 0);
        Assert.True(inset.Left > 0 || inset.Right > 0 || inset.Bottom > 0);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_inset_is_measured_per_window_not_reused()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // A popup has no resize border; an overlapped window does. If an
        // inset were cached and reused, both would measure the same.
        using var overlapped = TestWindow.Create(TestWindow.Overlapped);
        using var popup = TestWindow.Create(TestWindow.Popup);

        FrameInset first = WindowFrame.MeasureInset(overlapped.Handle);
        FrameInset second = WindowFrame.MeasureInset(popup.Handle);
        FrameInset again = WindowFrame.MeasureInset(overlapped.Handle);

        output.WriteLine($"overlapped {first}, popup {second}, overlapped again {again}");

        Assert.NotEqual(first, second);
        Assert.Equal(FrameInset.None, second);
        Assert.Equal(first, again);
    }

    /// <summary>
    /// Win32 queries the tests use as oracles, independent of the platform
    /// layer's own calls.
    /// </summary>
    private static unsafe partial class Independent
    {
        private const uint SPI_GETWORKAREA = 0x0030;
        private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

        public static PixelRect PrimaryWorkArea()
        {
            Rect r;

            if (!SystemParametersInfo(SPI_GETWORKAREA, 0, &r, 0))
            {
                throw new InvalidOperationException("SPI_GETWORKAREA failed.");
            }

            return new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        }

        public static string? DeviceInterfacePath(string deviceName)
        {
            var device = new DisplayDevice { Cb = (uint)sizeof(DisplayDevice) };

            if (!EnumDisplayDevices(deviceName, 0, ref device, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                return null;
            }

            string path = new(device.DeviceId);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayDevice
        {
            public uint Cb;
            public fixed char DeviceName[32];
            public fixed char DeviceString[128];
            public uint StateFlags;
            public fixed char DeviceId[128];
            public fixed char DeviceKey[128];
        }

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SystemParametersInfo(uint action, uint param, Rect* value, uint winIni);

        [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumDisplayDevices(string device, uint index, ref DisplayDevice value, uint flags);
    }

    /// <summary>
    /// A bare Win32 window, shown and destroyed within one test.
    /// </summary>
    /// <remarks>
    /// Uses the predefined <c>STATIC</c> class so no window class is
    /// registered. Shown, because DWM reports frame bounds for a visible
    /// window; placed small in the primary display's corner and destroyed
    /// immediately after measuring.
    /// </remarks>
    private sealed partial class TestWindow : IDisposable
    {
        public const uint Overlapped = 0x00CF0000; // WS_OVERLAPPEDWINDOW
        public const uint Popup = 0x80000000;      // WS_POPUP

        private const uint WS_VISIBLE = 0x10000000;

        private TestWindow(nint handle) => Handle = handle;

        public nint Handle { get; }

        public static TestWindow Create(uint style)
        {
            nint handle = CreateWindowEx(0, "STATIC", "Noto platform test", style | WS_VISIBLE, 40, 40, 320, 240, 0, 0, 0, 0);

            if (handle == 0)
            {
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
            }

            return new TestWindow(handle);
        }

        public void Dispose() => _ = DestroyWindow(Handle);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateWindowEx(
            uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyWindow(nint hWnd);
    }
}
