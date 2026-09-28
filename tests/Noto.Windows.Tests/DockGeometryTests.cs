using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Docked-workspace geometry and width limits (#16 slices 1 and 2).
/// </summary>
/// <remarks>
/// <para>
/// Deterministic: every display here is constructed, not enumerated, so the
/// cases cover topologies this machine does not have — a taskbar on the top
/// or left, a 150% display, a very narrow work area.
/// </para>
/// <para>
/// The two <see cref="DevelopmentPair"/> displays reproduce the development
/// environment's measured layout, including the left display's negative X and
/// vertical offset. They are fixtures, not an assumption that such a layout
/// exists at runtime.
/// </para>
/// </remarks>
public sealed class DockGeometryTests
{
    // Primary: (0,0)-(1920,1080), taskbar at the bottom -> work (0,0)-(1920,1032).
    private static readonly DisplayMonitor Primary = Display(
        "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96, primary: true);

    // Left of primary, 78px lower: (-1920,78)-(0,1158), work (-1920,78)-(0,1110).
    private static readonly DisplayMonitor LeftOfPrimary = Display(
        "\\\\.\\DISPLAY5", new PixelRect(-1920, 78, 0, 1158), new PixelRect(-1920, 78, 0, 1110), 96);

    private static DisplayMonitor[] DevelopmentPair => [Primary, LeftOfPrimary];

    // ------------------------------------------------------------ edges

    [Fact]
    public void Docks_flush_right_across_the_full_work_area_height()
    {
        WindowPlacement placed = DockGeometry.VisibleBounds(Primary, DockEdge.Right, 360);

        Assert.Equal(new PixelRect(1560, 0, 1920, 1032), placed.Bounds);
    }

    [Fact]
    public void Docks_flush_left_across_the_full_work_area_height()
    {
        WindowPlacement placed = DockGeometry.VisibleBounds(Primary, DockEdge.Left, 360);

        Assert.Equal(new PixelRect(0, 0, 360, 1032), placed.Bounds);
    }

    [Fact]
    public void Left_and_right_are_different_rectangles_of_the_same_size()
    {
        // Kills an edge swap: both edges produce a 360x1032 rectangle, so only
        // position distinguishes them.
        PixelRect left = DockGeometry.VisibleBounds(Primary, DockEdge.Left, 360).Bounds;
        PixelRect right = DockGeometry.VisibleBounds(Primary, DockEdge.Right, 360).Bounds;

        Assert.Equal(left.Width, right.Width);
        Assert.NotEqual(left, right);
        Assert.Equal(Primary.WorkArea.Left, left.Left);
        Assert.Equal(Primary.WorkArea.Right, right.Right);
    }

    [Fact]
    public void An_undefined_edge_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockGeometry.VisibleBounds(Primary, (DockEdge)7, 360));
    }

    // ------------------------------------------------------- work area

    [Fact]
    public void Height_comes_from_the_work_area_not_the_monitor_bounds()
    {
        // A bottom taskbar: bounds reach 1080, the work area stops at 1032.
        // Using the bounds would put the workspace under the taskbar.
        PixelRect placed = DockGeometry.VisibleBounds(Primary, DockEdge.Right, 360).Bounds;

        Assert.Equal(1032, placed.Bottom);
        Assert.NotEqual(Primary.Bounds.Bottom, placed.Bottom);
    }

    [Fact]
    public void A_top_taskbar_moves_the_workspace_down()
    {
        DisplayMonitor topTaskbar = Display(
            "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 48, 1920, 1080), 96);

        PixelRect placed = DockGeometry.VisibleBounds(topTaskbar, DockEdge.Right, 360).Bounds;

        Assert.Equal(new PixelRect(1560, 48, 1920, 1080), placed);
    }

    [Fact]
    public void A_left_taskbar_moves_a_left_docked_workspace_across()
    {
        DisplayMonitor leftTaskbar = Display(
            "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(62, 0, 1920, 1080), 96);

        PixelRect placed = DockGeometry.VisibleBounds(leftTaskbar, DockEdge.Left, 360).Bounds;

        Assert.Equal(new PixelRect(62, 0, 422, 1080), placed);
    }

    // ------------------------------------------- negative coordinates

    [Fact]
    public void Docks_right_on_a_display_with_a_negative_origin()
    {
        // The right edge of the left-hand display is x = 0; the workspace
        // occupies [-360, 0) and keeps the display's 78px vertical offset.
        PixelRect placed = DockGeometry.VisibleBounds(LeftOfPrimary, DockEdge.Right, 360).Bounds;

        Assert.Equal(new PixelRect(-360, 78, 0, 1110), placed);
    }

    [Fact]
    public void Docks_left_on_a_display_with_a_negative_origin()
    {
        PixelRect placed = DockGeometry.VisibleBounds(LeftOfPrimary, DockEdge.Left, 360).Bounds;

        Assert.Equal(new PixelRect(-1920, 78, -1560, 1110), placed);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Every_placement_stays_inside_its_own_work_area(DockEdge edge)
    {
        // Across both development displays. Kills any computation that
        // assumes an origin of (0, 0): on the left display it would land the
        // workspace on the primary instead.
        foreach (DisplayMonitor display in DevelopmentPair)
        {
            PixelRect placed = DockGeometry.VisibleBounds(display, edge, 360).Bounds;
            PixelRect work = display.WorkArea;

            Assert.InRange(placed.Left, work.Left, work.Right);
            Assert.InRange(placed.Right, work.Left, work.Right);
            Assert.Equal(work.Top, placed.Top);
            Assert.Equal(work.Bottom, placed.Bottom);
        }
    }

    [Fact]
    public void The_placement_names_the_display_and_carries_its_dpi()
    {
        WindowPlacement placed = DockGeometry.VisibleBounds(LeftOfPrimary, DockEdge.Right, 360);

        Assert.Equal("\\\\.\\DISPLAY5", placed.DeviceName);
        Assert.Equal(96u, placed.Dpi);
    }

    // ---------------------------------------------------------- DPI

    [Fact]
    public void Width_is_authored_in_dips_and_scaled_to_the_display()
    {
        // 150%: 360 DIP is 540 physical pixels. A pixel-authored width would
        // come out at 360 here and look a third narrower than on the primary.
        DisplayMonitor scaled = Display(
            "\\\\.\\DISPLAY2", new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 144);

        PixelRect placed = DockGeometry.VisibleBounds(scaled, DockEdge.Right, 360).Bounds;

        Assert.Equal(540, placed.Width);
        Assert.Equal(new PixelRect(2340, 0, 2880, 1560), placed);
    }

    [Fact]
    public void The_ceiling_is_applied_in_dips_not_pixels()
    {
        // 150%, work area 2880 px = 1920 DIP. Max is min(960, 900) = 900 DIP
        // = 1350 px. Comparing the ceiling against pixels would stop at 900 px.
        DisplayMonitor scaled = Display(
            "\\\\.\\DISPLAY2", new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 144);

        PixelRect placed = DockGeometry.VisibleBounds(scaled, DockEdge.Left, 5000).Bounds;

        Assert.Equal(1350, placed.Width);
    }

    [Theory]
    [InlineData(441, 168)]   // 175%, 252 DIP: narrower than 480, the minimum takes precedence
    [InlineData(300, 144)]   // 150%, 200 DIP: narrower than the minimum itself
    [InlineData(1920, 96)]
    [InlineData(2880, 144)]
    [InlineData(2400, 120)]
    [InlineData(3840, 192)]
    public void The_workspace_never_exceeds_its_work_area_in_pixels(int workWidthPx, uint dpi)
    {
        // The property that makes a separate pixel cap unnecessary: the clamp
        // keeps the width at or under the work area in DIPs, and converting
        // back to pixels at this display's scale cannot exceed it either.
        DisplayMonitor display = Display(
            "\\\\.\\DISPLAY3", new PixelRect(0, 0, workWidthPx, 1000), new PixelRect(0, 0, workWidthPx, 1000), dpi);

        foreach (double requested in new[] { 0.0, 240, 360, 900, 5000 })
        {
            int width = DockGeometry.VisibleBounds(display, DockEdge.Right, requested).Bounds.Width;

            Assert.InRange(width, 1, workWidthPx);
        }
    }

    [Fact]
    public void On_a_narrow_display_the_minimum_takes_precedence_over_half_the_work_area()
    {
        // 441 px at 175% is 252 DIP. Half would be 126 DIP; the adopted rule
        // raises the upper bound to the 240 DIP minimum instead: 420 px.
        DisplayMonitor narrow = Display(
            "\\\\.\\DISPLAY3", new PixelRect(0, 0, 441, 800), new PixelRect(0, 0, 441, 800), 168);

        Assert.Equal(420, DockGeometry.VisibleBounds(narrow, DockEdge.Right, 360).Bounds.Width);
    }

    [Fact]
    public void On_a_display_narrower_than_the_minimum_the_workspace_fills_the_work_area()
    {
        // 300 px at 150% is 200 DIP, below the 240 minimum: the physical work
        // area takes precedence, so the workspace is exactly the work area.
        DisplayMonitor tiny = Display(
            "\\\\.\\DISPLAY3", new PixelRect(0, 0, 300, 800), new PixelRect(0, 0, 300, 800), 144);

        Assert.Equal(new PixelRect(0, 0, 300, 800), DockGeometry.VisibleBounds(tiny, DockEdge.Left, 360).Bounds);
    }

    // ------------------------------------------------- width clamping

    [Theory]
    [InlineData(360, 1920, 360)]   // inside the range: untouched
    [InlineData(100, 1920, 240)]   // below the minimum
    [InlineData(240, 1920, 240)]   // exactly the minimum
    [InlineData(800, 1200, 600)]   // above half the work area
    [InlineData(600, 1200, 600)]   // exactly half
    [InlineData(1500, 3840, 900)]  // above the ceiling on a wide display
    [InlineData(900, 3840, 900)]   // exactly the ceiling
    public void Width_is_clamped_to_the_minimum_and_the_display_maximum(
        double requested, double workAreaDip, double expected)
    {
        Assert.Equal(expected, WorkspaceWidth.Clamp(requested, workAreaDip));
    }

    [Theory]
    [InlineData(1920, 900)]  // half is 960; the ceiling is lower
    [InlineData(1200, 600)]  // half is lower than the ceiling
    [InlineData(1800, 900)]  // half equals the ceiling
    public void The_maximum_is_the_lower_of_half_the_work_area_and_the_ceiling(double workAreaDip, double expected)
    {
        Assert.Equal(expected, WorkspaceWidth.MaximumFor(workAreaDip));
    }

    [Theory]
    [InlineData(1920, 240, 900)]  // normal: nominal limits apply
    [InlineData(800, 240, 400)]   // half the work area is the maximum
    [InlineData(480, 240, 240)]   // half meets the minimum exactly
    [InlineData(432, 240, 240)]   // half (216) is below the minimum: the minimum wins
    [InlineData(200, 200, 200)]   // below the minimum itself: the work area wins
    public void Effective_bounds_follow_the_adopted_contract(double workAreaDip, double minimum, double maximum)
    {
        // ADR-007 §4: effective minimum min(240, w); effective maximum
        // min(w, max(240, min(w / 2, 900))).
        Assert.Equal(minimum, WorkspaceWidth.EffectiveMinimumFor(workAreaDip));
        Assert.Equal(maximum, WorkspaceWidth.EffectiveMaximumFor(workAreaDip));
    }

    [Theory]
    [InlineData(100, 1920, 240)]    // below the effective minimum
    [InlineData(5000, 1920, 900)]   // above it: the 900 DIP cap
    [InlineData(100, 800, 240)]
    [InlineData(5000, 800, 400)]    // above it: half the work area
    [InlineData(100, 480, 240)]
    [InlineData(5000, 480, 240)]
    [InlineData(100, 432, 240)]     // narrow: fixed at the minimum from both sides
    [InlineData(5000, 432, 240)]
    [InlineData(0, 200, 200)]       // below the minimum: fixed at the work area from both sides
    [InlineData(5000, 200, 200)]
    public void A_width_outside_the_effective_bounds_is_brought_inside_them(
        double requested, double workAreaDip, double expected)
    {
        Assert.Equal(expected, WorkspaceWidth.Clamp(requested, workAreaDip));
    }

    [Fact]
    public void The_effective_bounds_are_always_a_valid_interval_inside_the_work_area()
    {
        // The invariants the adopted rule exists to guarantee, across both
        // breakpoints (240 and 480), the ceiling's (1800) and far beyond.
        double[] workAreas = [1, 50, 199.5, 200, 239.9, 240, 240.1, 300, 432, 479.9, 480, 480.1, 800, 1799, 1800, 1801, 1920, 3840, 7680];
        double[] requests = [-100, 0, 100, 239.9, 240, 360, 899, 900, 901, 5000];

        foreach (double w in workAreas)
        {
            double minimum = WorkspaceWidth.EffectiveMinimumFor(w);
            double maximum = WorkspaceWidth.EffectiveMaximumFor(w);

            Assert.True(minimum <= maximum, $"work area {w}: minimum {minimum} > maximum {maximum}");
            Assert.True(maximum <= w, $"work area {w}: maximum {maximum} exceeds it");

            foreach (double requested in requests)
            {
                double width = WorkspaceWidth.Clamp(requested, w);

                Assert.InRange(width, minimum, maximum);
                Assert.True(width >= 0, $"work area {w}, requested {requested}: width {width} < 0");
                Assert.True(width <= w, $"work area {w}, requested {requested}: width {width} exceeds it");
            }
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_width_is_rejected(double requested)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceWidth.Clamp(requested, 1920));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1920)]
    [InlineData(double.NaN)]
    public void A_work_area_width_that_is_not_positive_and_finite_is_rejected(double workAreaDip)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceWidth.MaximumFor(workAreaDip));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceWidth.EffectiveMinimumFor(workAreaDip));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceWidth.EffectiveMaximumFor(workAreaDip));
    }

    [Fact]
    public void The_width_limits_are_the_contracted_values()
    {
        // Pinned because they are provisional judgement values: changing one
        // should be a visible decision, not an incidental edit.
        Assert.Equal(240, WorkspaceWidth.MinimumDip);
        Assert.Equal(900, WorkspaceWidth.CeilingDip);
        Assert.Equal(0.5, WorkspaceWidth.MaximumWorkAreaFraction);
    }

    // ------------------------------------------ outer bounds (slice 2)

    // The inset the window-behaviour spike measured, and the one a real
    // overlapped window reports on this machine at 96 DPI.
    private static readonly FrameInset Measured = new(7, 0, 7, 7);

    // Every edge different, so an inset applied to the wrong side, or with the
    // wrong sign, cannot produce the right rectangle by coincidence.
    private static readonly FrameInset Lopsided = new(3, 5, 11, 13);

    [Fact]
    public void Right_docked_outer_bounds_extend_past_the_work_area_by_exactly_the_inset()
    {
        // Visible (1560,0)-(1920,1032): flush with the right edge and the
        // taskbar. The outer rectangle AppWindow.MoveAndResize takes is 7px
        // wider on each side and 7px taller, all of it invisible border.
        PixelRect outer = DockGeometry.OuterBounds(Primary, DockEdge.Right, 360, Measured);

        Assert.Equal(new PixelRect(1553, 0, 1927, 1039), outer);
    }

    [Fact]
    public void Left_docked_outer_bounds_start_at_a_negative_x_on_a_zero_origin_display()
    {
        // The invisible left border sits off-screen at x = -7. A position
        // clamped to zero would leave a visible 7px gap at the edge.
        PixelRect outer = DockGeometry.OuterBounds(Primary, DockEdge.Left, 360, Measured);

        Assert.Equal(new PixelRect(-7, 0, 367, 1039), outer);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void The_inset_is_added_on_each_side_it_was_measured_on(DockEdge edge)
    {
        PixelRect visible = DockGeometry.VisibleBounds(Primary, edge, 360).Bounds;

        PixelRect outer = DockGeometry.OuterBounds(Primary, edge, 360, Lopsided);

        Assert.Equal(visible.Left - 3, outer.Left);
        Assert.Equal(visible.Top - 5, outer.Top);
        Assert.Equal(visible.Right + 11, outer.Right);
        Assert.Equal(visible.Bottom + 13, outer.Bottom);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Without_an_inset_the_outer_bounds_are_the_visible_bounds(DockEdge edge)
    {
        // A popup-style window has no invisible border; nothing may be added.
        Assert.Equal(
            DockGeometry.VisibleBounds(Primary, edge, 360).Bounds,
            DockGeometry.OuterBounds(Primary, edge, 360, FrameInset.None));
    }

    [Fact]
    public void Right_docked_outer_bounds_on_a_negative_origin_display()
    {
        // Visible [-360, 0) at the display's 78px offset. The right border
        // overhangs onto the primary, where it is invisible.
        PixelRect outer = DockGeometry.OuterBounds(LeftOfPrimary, DockEdge.Right, 360, Measured);

        Assert.Equal(new PixelRect(-367, 78, 7, 1117), outer);
    }

    [Fact]
    public void Left_docked_outer_bounds_on_a_negative_origin_display()
    {
        PixelRect outer = DockGeometry.OuterBounds(LeftOfPrimary, DockEdge.Left, 360, Measured);

        Assert.Equal(new PixelRect(-1927, 78, -1553, 1117), outer);
    }

    [Fact]
    public void Outer_bounds_follow_a_top_taskbar()
    {
        // The work area starts at y = 48; the top inset is subtracted from
        // that, not from the display's top.
        DisplayMonitor topTaskbar = Display(
            "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 48, 1920, 1080), 96);

        PixelRect outer = DockGeometry.OuterBounds(topTaskbar, DockEdge.Right, 360, Lopsided);

        Assert.Equal(new PixelRect(1557, 43, 1931, 1093), outer);
    }

    [Fact]
    public void Outer_width_is_the_scaled_width_plus_both_side_insets()
    {
        // 150%: 360 DIP is 540 px of visible frame, plus the two side borders.
        // An inset measured at that scale is passed in; it is never scaled here.
        DisplayMonitor scaled = Display(
            "\\\\.\\DISPLAY2", new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 144);
        var insetAt150 = new FrameInset(10, 0, 10, 10);

        PixelRect outer = DockGeometry.OuterBounds(scaled, DockEdge.Right, 360, insetAt150);

        Assert.Equal(new PixelRect(2330, 0, 2890, 1570), outer);
        Assert.Equal(540 + 10 + 10, outer.Width);
    }

    [Theory]
    [InlineData(DockEdge.Left, 100)]
    [InlineData(DockEdge.Left, 360)]
    [InlineData(DockEdge.Left, 5000)]
    [InlineData(DockEdge.Right, 100)]
    [InlineData(DockEdge.Right, 360)]
    [InlineData(DockEdge.Right, 5000)]
    public void The_visible_frame_inside_the_outer_bounds_touches_the_edge_and_stays_in_the_work_area(
        DockEdge edge, double requestedDip)
    {
        // What the user sees is the outer rectangle less the inset. For every
        // display, edge and width — including widths the clamp must correct —
        // that frame spans the work area's height, touches the chosen edge
        // exactly, and never leaves the work area.
        foreach (DisplayMonitor display in DevelopmentPair)
        {
            PixelRect outer = DockGeometry.OuterBounds(display, edge, requestedDip, Lopsided);
            PixelRect seen = new(
                outer.Left + Lopsided.Left,
                outer.Top + Lopsided.Top,
                outer.Right - Lopsided.Right,
                outer.Bottom - Lopsided.Bottom);
            PixelRect work = display.WorkArea;

            Assert.Equal(work.Top, seen.Top);
            Assert.Equal(work.Bottom, seen.Bottom);
            Assert.True(seen.Left >= work.Left && seen.Right <= work.Right, $"{display.DeviceName} {edge}: {seen} leaves {work}");
            Assert.Equal(edge == DockEdge.Left ? work.Left : work.Right, edge == DockEdge.Left ? seen.Left : seen.Right);
            Assert.Equal(
                (int)Math.Round(WorkspaceWidth.Clamp(requestedDip, work.Width)),
                seen.Width);
        }
    }

    [Fact]
    public void Outer_bounds_reject_an_undefined_edge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockGeometry.OuterBounds(Primary, (DockEdge)7, 360, Measured));
    }

    // --------------------------------------------------- helpers

    private static DisplayMonitor Display(string device, PixelRect bounds, PixelRect work, uint dpi, bool primary = false) =>
        new(MonitorId.From(device + "#id"), device, bounds, work, dpi, primary);
}
