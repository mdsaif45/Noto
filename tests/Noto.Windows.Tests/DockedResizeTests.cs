using Noto.Core.Workspace;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Docked resize geometry, the hit-test rule and the edge mapping (#16 slice 3).
/// </summary>
/// <remarks>
/// Deterministic: displays are constructed, so the cases cover topologies this
/// machine does not have. The fixtures match <see cref="DockGeometryTests"/>.
/// </remarks>
public sealed class DockedResizeTests
{
    // Primary: (0,0)-(1920,1080), bottom taskbar -> work (0,0)-(1920,1032).
    private static readonly DisplayMonitor Primary = Display(
        "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96);

    // Left of primary, 78px lower: work (-1920,78)-(0,1110).
    private static readonly DisplayMonitor LeftOfPrimary = Display(
        "\\\\.\\DISPLAY5", new PixelRect(-1920, 78, 0, 1158), new PixelRect(-1920, 78, 0, 1110), 96);

    private static readonly FrameInset Measured = new(7, 0, 7, 7);

    // Every edge different, so a side or sign error cannot pass by coincidence.
    private static readonly FrameInset Lopsided = new(3, 5, 11, 13);

    // ------------------------------------------------------ edge mapping

    [Theory]
    [InlineData(WorkspaceEdge.Left, DockEdge.Left)]
    [InlineData(WorkspaceEdge.Right, DockEdge.Right)]
    public void A_workspace_edge_maps_to_the_same_dock_edge(WorkspaceEdge setting, DockEdge expected)
    {
        Assert.Equal(expected, DockEdges.From(setting));
    }

    [Fact]
    public void An_undefined_workspace_edge_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DockEdges.From((WorkspaceEdge)9));
    }

    // -------------------------------------------------- resize geometry

    [Fact]
    public void Dragging_a_right_docked_window_wider_moves_only_its_left_edge()
    {
        // Proposed: the user dragged the left edge 200px left from 360 visible.
        var proposed = new PixelRect(1353, 0, 1927, 1039);

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, DockEdge.Right, proposed, Measured);

        Assert.Equal(new PixelRect(1353, 0, 1927, 1039), outer);
    }

    [Fact]
    public void Dragging_a_left_docked_window_wider_moves_only_its_right_edge()
    {
        var proposed = new PixelRect(-7, 0, 567, 1039);

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, DockEdge.Left, proposed, Measured);

        Assert.Equal(new PixelRect(-7, 0, 567, 1039), outer);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void The_docked_edge_and_the_height_never_move_whatever_the_drag_proposes(DockEdge edge)
    {
        // A proposal that is off the edge, short and offset vertically. Only
        // its width may survive.
        var proposed = new PixelRect(400, 300, 900, 700);

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, edge, proposed, Measured);
        PixelRect visible = Visible(outer, Measured);

        Assert.Equal(0, visible.Top);
        Assert.Equal(1032, visible.Bottom);
        Assert.Equal(edge == DockEdge.Left ? 0 : 1920, edge == DockEdge.Left ? visible.Left : visible.Right);
        Assert.Equal(500 - 7 - 7, visible.Width);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void A_drag_past_the_maximum_stops_at_900_dip_visible(DockEdge edge)
    {
        var proposed = new PixelRect(0, 0, 1900, 1039);

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, edge, proposed, Measured);

        Assert.Equal(900, Visible(outer, Measured).Width);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void A_drag_past_the_minimum_stops_at_240_dip_visible(DockEdge edge)
    {
        var proposed = new PixelRect(0, 0, 50, 1039);

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, edge, proposed, Measured);

        Assert.Equal(240, Visible(outer, Measured).Width);
    }

    [Fact]
    public void The_visible_width_is_what_is_clamped_not_the_outer_width()
    {
        // 254 outer = 240 visible with 7+7 of border: exactly the minimum, so
        // nothing moves. Clamping the outer width would push it to 240 outer
        // and 226 visible.
        var proposed = new PixelRect(1673, 0, 1927, 1039);

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, DockEdge.Right, proposed, Measured);

        Assert.Equal(proposed, outer);
        Assert.Equal(240, Visible(outer, Measured).Width);
    }

    [Fact]
    public void The_inset_is_applied_on_each_side_it_was_measured_on()
    {
        var proposed = new PixelRect(1400, 0, 1934, 1045);   // 534 outer = 520 visible with 3+11

        PixelRect outer = DockGeometry.ResizedOuterBounds(Primary, DockEdge.Right, proposed, Lopsided);

        Assert.Equal(new PixelRect(1920 - 520 - 3, 0 - 5, 1920 + 11, 1032 + 13), outer);
    }

    [Fact]
    public void Limits_are_dips_so_a_scaled_display_clamps_at_scaled_pixels()
    {
        // 150%: 240 DIP is 360 px and 900 DIP is 1350 px. Pixel-authored
        // limits would stop at 240 px and 900 px.
        DisplayMonitor scaled = Display(
            "\\\\.\\DISPLAY2", new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 144);
        var inset = new FrameInset(10, 0, 10, 10);

        PixelRect wide = DockGeometry.ResizedOuterBounds(scaled, DockEdge.Right, new PixelRect(0, 0, 2800, 1570), inset);
        PixelRect narrow = DockGeometry.ResizedOuterBounds(scaled, DockEdge.Right, new PixelRect(0, 0, 100, 1570), inset);

        Assert.Equal(1350, Visible(wide, inset).Width);
        Assert.Equal(360, Visible(narrow, inset).Width);
    }

    [Fact]
    public void A_width_inside_the_limits_is_kept_exactly_on_a_scaled_display()
    {
        // 150%: 600 visible px is 400 DIP, inside [240, 900], so it is kept.
        // At the limits pixels and DIPs clamp to the same place; only an
        // in-range width shows which unit the clamp worked in — treating the
        // 600 as DIPs would widen the window to 900 px.
        DisplayMonitor scaled = Display(
            "\\\\.\\DISPLAY2", new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 144);
        var inset = new FrameInset(10, 0, 10, 10);

        PixelRect outer = DockGeometry.ResizedOuterBounds(scaled, DockEdge.Right, new PixelRect(0, 0, 620, 1570), inset);

        Assert.Equal(600, Visible(outer, inset).Width);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void On_a_display_with_a_negative_origin_the_dock_stays_on_that_display(DockEdge edge)
    {
        var proposed = new PixelRect(-900, 0, -300, 1000);   // 600 outer

        PixelRect outer = DockGeometry.ResizedOuterBounds(LeftOfPrimary, edge, proposed, Measured);
        PixelRect visible = Visible(outer, Measured);

        Assert.Equal(78, visible.Top);
        Assert.Equal(1110, visible.Bottom);
        Assert.Equal(edge == DockEdge.Left ? -1920 : 0, edge == DockEdge.Left ? visible.Left : visible.Right);
        Assert.Equal(586, visible.Width);
    }

    [Fact]
    public void On_a_work_area_narrower_than_the_minimum_no_drag_changes_the_width()
    {
        // 200 DIP wide: effective minimum = maximum = 200.
        DisplayMonitor narrow = Display(
            "\\\\.\\DISPLAY3", new PixelRect(0, 0, 200, 800), new PixelRect(0, 0, 200, 800), 96);

        foreach (int proposedWidth in new[] { 20, 214, 900 })
        {
            PixelRect outer = DockGeometry.ResizedOuterBounds(narrow, DockEdge.Right, new PixelRect(0, 0, proposedWidth, 807), Measured);

            Assert.Equal(200, Visible(outer, Measured).Width);
        }
    }

    [Fact]
    public void An_undefined_edge_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockGeometry.ResizedOuterBounds(Primary, (DockEdge)7, new PixelRect(0, 0, 500, 1039), Measured));
    }

    // ---------------------------------------------------------- hit test

    // WM_NCHITTEST results, as Windows defines them.
    private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTCLOSE = 20;

    [Theory]
    [InlineData(HTLEFT, HTLEFT)]            // the inner edge stays a resize edge
    [InlineData(HTTOPLEFT, HTLEFT)]         // its corners become that edge alone
    [InlineData(HTBOTTOMLEFT, HTLEFT)]
    [InlineData(HTRIGHT, HTCLIENT)]         // the docked edge does not resize
    [InlineData(HTTOPRIGHT, HTCLIENT)]
    [InlineData(HTBOTTOMRIGHT, HTCLIENT)]
    [InlineData(HTTOP, HTCLIENT)]           // top and bottom do not resize
    [InlineData(HTBOTTOM, HTCLIENT)]
    [InlineData(HTCLIENT, HTCLIENT)]        // everything else is untouched
    [InlineData(HTCAPTION, HTCAPTION)]
    [InlineData(HTCLOSE, HTCLOSE)]
    public void A_right_docked_window_resizes_only_from_its_left_edge(int hit, int expected)
    {
        Assert.Equal(expected, DockedWindow.MapHitTest(DockEdge.Right, hit));
    }

    [Theory]
    [InlineData(HTRIGHT, HTRIGHT)]
    [InlineData(HTTOPRIGHT, HTRIGHT)]
    [InlineData(HTBOTTOMRIGHT, HTRIGHT)]
    [InlineData(HTLEFT, HTCLIENT)]
    [InlineData(HTTOPLEFT, HTCLIENT)]
    [InlineData(HTBOTTOMLEFT, HTCLIENT)]
    [InlineData(HTTOP, HTCLIENT)]
    [InlineData(HTBOTTOM, HTCLIENT)]
    [InlineData(HTCLIENT, HTCLIENT)]
    [InlineData(HTCAPTION, HTCAPTION)]
    public void A_left_docked_window_resizes_only_from_its_right_edge(int hit, int expected)
    {
        Assert.Equal(expected, DockedWindow.MapHitTest(DockEdge.Left, hit));
    }

    // ------------------------------------------------------------ helpers

    private static PixelRect Visible(PixelRect outer, FrameInset inset) => new(
        outer.Left + inset.Left, outer.Top + inset.Top, outer.Right - inset.Right, outer.Bottom - inset.Bottom);

    private static DisplayMonitor Display(string device, PixelRect bounds, PixelRect work, uint dpi) =>
        new(MonitorId.From(device + "#id"), device, bounds, work, dpi, isPrimary: false);
}
