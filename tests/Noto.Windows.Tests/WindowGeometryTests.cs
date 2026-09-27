using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The platform geometry value types (ADR-007).
/// </summary>
/// <remarks>
/// <para>
/// These are the whole of <c>Noto.Platform.Windows</c> today: three value
/// types and seven members. Edge-rect calculation, work-area handling, width
/// clamping and monitor enumeration <b>do not exist yet</b> — they are #16
/// slice 1 — so nothing here tests them. Tests written against absent code
/// would have had to invent the code first.
/// </para>
/// <para>
/// Small as it is, this is the arithmetic every docked window position will be
/// built on, and the spike found a real defect in exactly this area: naive
/// positioning left a 7px gap because <c>MoveAndResize</c> takes outer
/// coordinates. These tests pin the conversions that fix it.
/// </para>
/// </remarks>
public sealed class WindowGeometryTests
{
    // ------------------------------------------------------------ PixelRect

    [Theory]
    [InlineData(0, 0, 1920, 1080, 1920, 1080)]   // full HD, origin
    [InlineData(1600, 0, 1920, 1032, 320, 1032)] // the spike's right-edge dock
    [InlineData(0, 0, 0, 0, 0, 0)]               // degenerate
    [InlineData(-1920, 0, 0, 1080, 1920, 1080)]  // monitor left of primary
    public void A_rect_measures_itself_from_its_edges(
        int left, int top, int right, int bottom, int width, int height)
    {
        // Negative coordinates are ordinary: a second monitor placed to the
        // left of the primary has a negative origin in virtual screen space,
        // and width must still be positive there.
        var rect = new PixelRect(left, top, right, bottom);

        Assert.Equal(width, rect.Width);
        Assert.Equal(height, rect.Height);
    }

    [Fact]
    public void Width_is_right_minus_left_not_the_reverse()
    {
        // Adversarial: the symmetric 1920x1080-at-origin case passes with the
        // subtraction inverted, because -1920 is never compared. An
        // asymmetric, offset rect distinguishes them.
        var rect = new PixelRect(1600, 24, 1920, 1032);

        Assert.Equal(320, rect.Width);
        Assert.Equal(1008, rect.Height);
    }

    [Fact]
    public void A_rect_describes_itself_for_diagnostics()
    {
        // The spike's evidence was read off strings in exactly this shape, and
        // ADR-007 quotes them. Worth pinning: a change here makes the recorded
        // measurements unreadable against a future run.
        Assert.Equal(
            "(1600,0)-(1920,1032) 320x1032",
            new PixelRect(1600, 0, 1920, 1032).ToString());
    }

    // ----------------------------------------------------------- FrameInset

    [Fact]
    public void No_inset_leaves_a_rectangle_untouched()
    {
        var visible = new PixelRect(1600, 0, 1920, 1032);

        Assert.Equal(visible, FrameInset.None.ToOuter(visible));
    }

    [Fact]
    public void No_inset_is_zero_on_every_edge()
    {
        Assert.Equal(new FrameInset(0, 0, 0, 0), FrameInset.None);
    }

    [Fact]
    public void The_outer_rectangle_grows_outward_on_every_edge()
    {
        // THE regression test for this file. The spike measured 7/0/7/7 on
        // Windows 11 26200 at 96 DPI and found that positioning without
        // compensating leaves the panel 7px from the screen edge and 14px
        // narrow — a visible defect, easy to miss.
        //
        // Outer must be LARGER than visible: left/top move back, right/bottom
        // move forward. A sign flip anywhere inverts that, and the numbers
        // below are asymmetric precisely so a flip cannot cancel out.
        var inset = new FrameInset(7, 0, 7, 7);
        var visible = new PixelRect(1600, 0, 1920, 1032);

        PixelRect outer = inset.ToOuter(visible);

        Assert.Equal(new PixelRect(1593, 0, 1927, 1039), outer);

        // Stated as a property too, so the intent survives a renumbering.
        Assert.True(outer.Left < visible.Left, "left edge must move outward");
        Assert.True(outer.Right > visible.Right, "right edge must move outward");
        Assert.True(outer.Bottom > visible.Bottom, "bottom edge must move outward");
        Assert.Equal(visible.Top, outer.Top);
    }

    [Fact]
    public void A_non_uniform_inset_is_applied_per_edge()
    {
        // ADR-007: the inset "was 7/0/7/7 — NOT uniform". Four distinct values
        // here, so applying one edge's value to another cannot pass.
        var inset = new FrameInset(1, 2, 3, 4);

        Assert.Equal(
            new PixelRect(99, 98, 203, 204),
            inset.ToOuter(new PixelRect(100, 100, 200, 200)));
    }

    [Fact]
    public void Compensating_then_measuring_recovers_the_requested_width()
    {
        // The property the spike's fix depends on: the visible rect the user
        // asked for is recoverable from the outer rect that was submitted.
        var inset = new FrameInset(7, 0, 7, 7);
        var requested = new PixelRect(1600, 0, 1920, 1032);

        PixelRect outer = inset.ToOuter(requested);

        Assert.Equal(requested.Width + inset.Left + inset.Right, outer.Width);
        Assert.Equal(requested.Height + inset.Top + inset.Bottom, outer.Height);
    }

    // ------------------------------------------------------- WindowPlacement

    [Theory]
    [InlineData(96u, 1.0)]
    [InlineData(120u, 1.25)]
    [InlineData(144u, 1.5)]
    [InlineData(168u, 1.75)]  // the 175% case WinAppSDK 2.5.1 has a fix for
    [InlineData(192u, 2.0)]
    public void Scale_is_relative_to_96_dpi(uint dpi, double expected)
    {
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), dpi);

        Assert.Equal(expected, placement.Scale);
    }

    [Theory]
    [InlineData(96u, 320.0, 320)]   // 100% — DIPs are pixels
    [InlineData(120u, 320.0, 400)]  // 125%
    [InlineData(144u, 320.0, 480)]  // 150%
    [InlineData(192u, 320.0, 640)]  // 200%
    [InlineData(96u, 0.0, 0)]
    public void Dips_convert_to_pixels_at_the_placement_scale(uint dpi, double dip, int expected)
    {
        // #30: "panel width is authored in DIPs and scaled per-monitor — a
        // pixel width would change apparent size across displays." This is
        // that conversion, and it is why a stored width survives a monitor
        // change unchanged.
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), dpi);

        Assert.Equal(expected, placement.DipToPixels(dip));
    }

    [Fact]
    public void Dip_conversion_actually_uses_the_scale()
    {
        // Adversarial: every 96-DPI case passes with the scale factor dropped
        // entirely, since multiplying by 1.0 is a no-op. Only a non-unity
        // scale distinguishes "converts" from "returns the input rounded".
        var ninetySix = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), 96);
        var oneFortyFour = new WindowPlacement("\\\\.\\DISPLAY2", new PixelRect(0, 0, 1920, 1080), 144);

        Assert.Equal(320, ninetySix.DipToPixels(320));
        Assert.Equal(480, oneFortyFour.DipToPixels(320));
        Assert.NotEqual(ninetySix.DipToPixels(320), oneFortyFour.DipToPixels(320));
    }

    [Theory]
    [InlineData(0.5, 0)]    // to even  -> 0, not 1
    [InlineData(1.5, 2)]    // to even  -> 2
    [InlineData(2.5, 2)]    // to even  -> 2, not 3
    [InlineData(3.5, 4)]
    [InlineData(0.4, 0)]
    [InlineData(0.6, 1)]
    public void Halfway_values_round_to_even(double dip, int expected)
    {
        // MEASURED, not assumed. Math.Round defaults to banker's rounding, so
        // 0.5 -> 0 and 2.5 -> 2, which is NOT what "round half up" would give.
        // Pinned because a later switch to MidpointRounding.AwayFromZero would
        // shift a panel edge by one pixel at some scales, and nothing else in
        // the codebase would notice.
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), 96);

        Assert.Equal(expected, placement.DipToPixels(dip));
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(-1.5, -2)]
    [InlineData(-320.0, -320)]
    public void Negative_dips_convert_symmetrically(double dip, int expected)
    {
        // A monitor left of the primary has negative coordinates, so negative
        // DIPs are reachable in real placement arithmetic rather than a
        // hypothetical.
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), 96);

        Assert.Equal(expected, placement.DipToPixels(dip));
    }

    [Fact]
    public void A_placement_carries_the_display_it_describes()
    {
        // Placement is per-monitor by construction. #16 will compose a
        // per-monitor settings key from an opaque identity; this is the field
        // that makes the placement itself unambiguous meanwhile.
        var placement = new WindowPlacement("\\\\.\\DISPLAY2", new PixelRect(1920, 0, 3840, 1080), 144);

        Assert.Equal("\\\\.\\DISPLAY2", placement.DeviceName);
        Assert.Equal(1920, placement.Bounds.Width);
        Assert.Equal(1.5, placement.Scale);
    }
}
