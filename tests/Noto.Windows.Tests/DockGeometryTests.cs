using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Docked-workspace geometry and width limits (#16 slice 1).
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
    [InlineData(441, 168)]   // 175%, narrow: clamps to half of 252 DIP
    [InlineData(1920, 96)]
    [InlineData(2880, 144)]
    [InlineData(2400, 120)]
    [InlineData(3840, 192)]
    public void The_workspace_never_exceeds_half_its_work_area(int workWidthPx, uint dpi)
    {
        // The property that makes a separate "not past the work area" cap
        // unnecessary. At most one pixel over half, for rounding.
        DisplayMonitor display = Display(
            "\\\\.\\DISPLAY3", new PixelRect(0, 0, workWidthPx, 1000), new PixelRect(0, 0, workWidthPx, 1000), dpi);

        foreach (double requested in new[] { 0.0, 240, 360, 900, 5000 })
        {
            int width = DockGeometry.VisibleBounds(display, DockEdge.Right, requested).Bounds.Width;

            Assert.InRange(width, 1, (workWidthPx / 2) + 1);
        }
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

    [Fact]
    public void When_the_work_area_is_too_narrow_for_both_limits_half_the_work_area_wins()
    {
        // 400 DIP: half is 200, below the 240 minimum. The workspace keeps to
        // one side of the display rather than covering 60% of it.
        Assert.Equal(200, WorkspaceWidth.Clamp(360, 400));
        Assert.Equal(200, WorkspaceWidth.Clamp(100, 400));
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

    // --------------------------------------------------- helpers

    private static DisplayMonitor Display(string device, PixelRect bounds, PixelRect work, uint dpi, bool primary = false) =>
        new(MonitorId.From(device + "#id"), device, bounds, work, dpi, primary);
}
