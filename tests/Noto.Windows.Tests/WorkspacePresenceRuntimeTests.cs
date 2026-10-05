using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Reading where the window is, and restoring it, on real Win32 windows
/// (#16 slice 5). The foreground itself is not asserted: a test process may
/// hold foreground rights, so that is the runtime harness's job.
/// </summary>
public sealed partial class PlatformRuntimeTests
{
    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_hidden_window_reads_as_hidden_even_if_it_was_minimized()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = TestWindow.Create(TestWindow.Overlapped);

        _ = Native.ShowWindow(window.Handle, Native.SW_HIDE);
        Assert.Equal(WorkspacePresence.Hidden, WindowActivation.PresenceOf(window.Native));

        _ = Native.ShowWindow(window.Handle, Native.SW_SHOWMINNOACTIVE);
        Assert.Equal(WorkspacePresence.Minimized, WindowActivation.PresenceOf(window.Native));

        _ = Native.ShowWindow(window.Handle, Native.SW_HIDE);
        Assert.Equal(WorkspacePresence.Hidden, WindowActivation.PresenceOf(window.Native));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_shown_window_is_never_read_as_hidden_or_minimized()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = TestWindow.Create(TestWindow.Overlapped);

        WorkspacePresence presence = WindowActivation.PresenceOf(window.Native);

        output.WriteLine($"shown window reads as {presence}");
        Assert.Contains(presence, new[] { WorkspacePresence.Background, WorkspacePresence.Foreground });
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Restore_brings_a_minimized_window_back_to_normal()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = TestWindow.Create(TestWindow.Overlapped);
        _ = Native.SendMessage(window.Handle, Native.WM_SYSCOMMAND, Native.SC_MINIMIZE, 0);
        Assert.True(Native.IsIconic(window.Handle));

        Assert.True(WindowActivation.Restore(window.Native));
        Assert.False(Native.IsIconic(window.Handle));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Restore_never_shows_a_hidden_window()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = TestWindow.Create(TestWindow.Overlapped);
        _ = Native.ShowWindow(window.Handle, Native.SW_HIDE);

        Assert.True(WindowActivation.Restore(window.Native));
        Assert.Equal(WorkspacePresence.Hidden, WindowActivation.PresenceOf(window.Native));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_dock_reports_a_resize_only_inside_the_size_loop()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var window = DockedTestWindow(DockEdge.Right, out DockedWindow docked, out _);

        Assert.False(docked.IsResizing);
        docked.OnEnterSizeMove();
        Assert.True(docked.IsResizing);
        docked.OnExitSizeMove();
        Assert.False(docked.IsResizing);
    }

    private static partial class Native
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOWMINNOACTIVE = 7;
    }
}
