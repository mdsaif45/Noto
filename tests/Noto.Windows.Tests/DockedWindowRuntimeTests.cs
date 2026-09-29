using System.Runtime.InteropServices;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The docked-window subclass on real Win32 windows (#16 slice 3).
/// </summary>
/// <remarks>
/// <para>
/// Runtime, not simulated: a real subclass is installed on a real window and
/// real <c>SetWindowPos</c> calls go through it. The window is a bare
/// overlapped Win32 window, not Noto's WinUI window — this project may not
/// reach the XAML layer. What only the real application can show (the drag
/// itself, snapping, the WinUI caption) is covered by the pull request's
/// real-app campaign.
/// </para>
/// <para>
/// The drag-step and drag-end handlers are exercised through their internal
/// entry points, because sending <c>WM_SIZING</c> from a test needs a
/// raw <c>SendMessage</c> import. Their routing from the window procedure is
/// proven by the real-app campaign.
/// </para>
/// </remarks>
public sealed partial class PlatformRuntimeTests
{
    [Theory]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void An_external_reposition_is_sent_back_to_the_dock(DockEdge edge)
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(edge, out DockedWindow docked, out PixelRect dockedOuter);

        // Another caller moving and resizing it: refused.
        Assert.True(Native.SetWindowPos(window.Handle, 0, 100, 100, 800, 600, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
        PixelRect after = Independent.WindowRect(window.Handle);

        output.WriteLine($"{edge}: docked {dockedOuter}, after external SetWindowPos {after}");

        Assert.Equal(dockedOuter, after);
        Assert.True(docked.IsAttached);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_move_only_request_is_refused_too()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(DockEdge.Right, out _, out PixelRect dockedOuter);

        Assert.True(Native.SetWindowPos(window.Handle, 0, 10, 10, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));

        Assert.Equal(dockedOuter, Independent.WindowRect(window.Handle));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Nothing_is_refused_when_neither_position_nor_size_changes()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // A z-order or show-state change must pass untouched, not be turned
        // into a move. The window is first put somewhere off the dock (an own
        // move), so a guard that wrongly rewrote this call would visibly
        // move it back.
        using var window = DockedTestWindow(DockEdge.Right, out DockedWindow docked, out _);
        docked.MoveOwn(() => Native.SetWindowPos(window.Handle, 0, 100, 100, 800, 600, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
        PixelRect elsewhere = Independent.WindowRect(window.Handle);

        Assert.True(Native.SetWindowPos(window.Handle, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE));

        Assert.Equal(new PixelRect(100, 100, 900, 700), elsewhere);
        Assert.Equal(elsewhere, Independent.WindowRect(window.Handle));
    }

    [Theory]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Notos_own_moves_pass_the_guard(DockEdge edge)
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(edge, out DockedWindow docked, out _);
        var reports = 0;
        docked.Resized += (_, _) => reports++;

        DisplayMonitor display = DisplayMonitors.ForWindow(window.Native);
        PixelRect wider = DockGeometry.OuterBounds(display, edge, 500, WindowFrame.MeasureInset(window.Native));

        docked.MoveOwn(() => Assert.True(
            Native.SetWindowPos(window.Handle, 0, wider.Left, wider.Top, wider.Width, wider.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE)));

        Assert.Equal(wider, Independent.WindowRect(window.Handle));
        Assert.Equal(500, Independent.VisibleFrame(window.Handle).Width);

        // Noto moving its own window is not the user resizing it.
        Assert.Equal(0, reports);

        // And the guard is back on afterwards.
        Assert.True(Native.SetWindowPos(window.Handle, 0, 100, 100, 800, 600, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
        Assert.Equal(wider, Independent.WindowRect(window.Handle));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_guard_is_restored_even_if_an_own_move_throws()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(DockEdge.Right, out DockedWindow docked, out PixelRect dockedOuter);

        Assert.Throws<InvalidOperationException>(() => docked.MoveOwn(() => throw new InvalidOperationException()));

        Assert.True(Native.SetWindowPos(window.Handle, 0, 100, 100, 800, 600, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
        Assert.Equal(dockedOuter, Independent.WindowRect(window.Handle));
    }

    [Theory]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void A_finished_drag_reports_the_visible_width_in_dips_once(DockEdge edge)
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(edge, out DockedWindow docked, out PixelRect dockedOuter);
        var reports = new List<DockResizedEventArgs>();
        docked.Resized += (_, e) => reports.Add(e);

        docked.OnEnterSizeMove();
        PixelRect step = docked.OnSizing(new PixelRect(0, 0, dockedOuter.Width + 140, 10));
        docked.MoveOwn(() => Native.SetWindowPos(window.Handle, 0, step.Left, step.Top, step.Width, step.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
        docked.OnExitSizeMove();

        DisplayMonitor display = DisplayMonitors.ForWindow(window.Native);
        double expected = display.WorkAreaPlacement.PixelsToDip(Independent.VisibleFrame(window.Handle).Width);

        output.WriteLine($"{edge}: step {step}, visible {Independent.VisibleFrame(window.Handle)}, reported {reports.Single().VisibleWidthDip} DIP");

        DockResizedEventArgs report = Assert.Single(reports);
        Assert.Equal(expected, report.VisibleWidthDip);
        Assert.Equal(500, report.VisibleWidthDip);
        Assert.Equal(display.Id, report.Monitor);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_size_loop_with_no_resize_reports_nothing()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(DockEdge.Right, out DockedWindow docked, out _);
        var reports = 0;
        docked.Resized += (_, _) => reports++;

        // A press on the border that never moves.
        docked.OnEnterSizeMove();
        docked.OnExitSizeMove();

        Assert.Equal(0, reports);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void During_a_size_loop_the_steps_are_not_refused()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // WM_SIZING has already refitted each step; the position guard must
        // not fight it.
        using var window = DockedTestWindow(DockEdge.Right, out DockedWindow docked, out PixelRect dockedOuter);

        docked.OnEnterSizeMove();
        PixelRect step = docked.OnSizing(new PixelRect(0, 0, dockedOuter.Width + 140, 10));
        Assert.True(Native.SetWindowPos(window.Handle, 0, step.Left, step.Top, step.Width, step.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
        docked.OnExitSizeMove();

        Assert.Equal(step, Independent.WindowRect(window.Handle));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_subclass_is_removed_when_the_window_is_destroyed()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        DockedWindow docked;

        using (DockedTestWindow(DockEdge.Right, out docked, out _))
        {
            Assert.True(docked.IsAttached);
        }

        Assert.False(docked.IsAttached);
    }

    [Fact]
    public void A_null_window_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => DockedWindow.Attach(null!, DockEdge.Right));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void An_undefined_edge_is_rejected_for_a_real_window()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = TestWindow.Create(TestWindow.Overlapped);

        Assert.Throws<ArgumentOutOfRangeException>(() => DockedWindow.Attach(window.Native, (DockEdge)7));
    }

    /// <summary>A real overlapped window at its docked position, with the dock installed.</summary>
    private static TestWindow DockedTestWindow(DockEdge edge, out DockedWindow docked, out PixelRect dockedOuter)
    {
        PixelRect outer;

        using (var probe = TestWindow.Create(TestWindow.Overlapped))
        {
            outer = WindowDocking.OuterBoundsFor(probe.Native, edge, 360);
        }

        var window = TestWindow.Create(TestWindow.Overlapped, outer);
        docked = DockedWindow.Attach(window.Native, edge);
        dockedOuter = Independent.WindowRect(window.Handle);

        return window;
    }

    private static partial class Native
    {
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
