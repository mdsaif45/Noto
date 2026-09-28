using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Frame-inset measurement arithmetic, pixel→DIP conversion, monitor identity
/// and monitor validation (#16 slice 1). Deterministic; no desktop needed.
/// </summary>
public sealed class PlatformPrimitiveTests
{
    // ------------------------------------------------ FrameInset.Between

    [Fact]
    public void Measures_the_inset_the_spike_recorded()
    {
        // ADR-007: at 96 DPI on Windows 11 26200 the outer rectangle of a
        // right-docked panel was (1593,0)-(1927,1039) for a visible frame of
        // (1600,0)-(1920,1032): an inset of 7/0/7/7.
        FrameInset inset = FrameInset.Between(
            window: new PixelRect(1593, 0, 1927, 1039),
            visibleFrame: new PixelRect(1600, 0, 1920, 1032));

        Assert.Equal(new FrameInset(7, 0, 7, 7), inset);
    }

    [Fact]
    public void Each_edge_is_measured_independently()
    {
        // Four different values, so measuring one edge from another's pair of
        // coordinates — or swapping left for right — cannot pass.
        FrameInset inset = FrameInset.Between(
            window: new PixelRect(99, 98, 203, 204),
            visibleFrame: new PixelRect(100, 100, 200, 200));

        Assert.Equal(new FrameInset(1, 2, 3, 4), inset);
    }

    [Fact]
    public void A_frame_filling_the_window_measures_as_no_inset()
    {
        var rect = new PixelRect(10, 20, 330, 1052);

        Assert.Equal(FrameInset.None, FrameInset.Between(rect, rect));
    }

    [Fact]
    public void An_inset_on_a_negative_origin_display_is_measured_the_same_way()
    {
        // The left-hand development display. Coordinates are negative; the
        // inset is still frame-minus-window on the leading edges and
        // window-minus-frame on the trailing ones.
        FrameInset inset = FrameInset.Between(
            window: new PixelRect(-367, 78, 7, 1117),
            visibleFrame: new PixelRect(-360, 78, 0, 1110));

        Assert.Equal(new FrameInset(7, 0, 7, 7), inset);
    }

    [Theory]
    [InlineData(1593, 0, 1927, 1039, 1600, 0, 1920, 1032)]
    [InlineData(99, 98, 203, 204, 100, 100, 200, 200)]
    [InlineData(-367, 78, 7, 1117, -360, 78, 0, 1110)]
    public void Measuring_then_compensating_returns_the_original_window(
        int wl, int wt, int wr, int wb, int fl, int ft, int fr, int fb)
    {
        // Between and ToOuter are inverses. This is the property docking
        // relies on: measure the inset once, then ask for the outer rectangle
        // that yields a chosen visible frame.
        var window = new PixelRect(wl, wt, wr, wb);
        var frame = new PixelRect(fl, ft, fr, fb);

        Assert.Equal(window, FrameInset.Between(window, frame).ToOuter(frame));
    }

    [Fact]
    public void The_inset_is_returned_as_measured_not_clamped()
    {
        // A frame extending past its window is not documented Windows
        // behaviour, but if it were ever reported, hiding it by clamping to
        // zero would hide the evidence.
        FrameInset inset = FrameInset.Between(
            window: new PixelRect(0, 0, 100, 100),
            visibleFrame: new PixelRect(-2, 0, 100, 100));

        Assert.Equal(-2, inset.Left);
    }

    // --------------------------------------------------- PixelsToDip

    [Theory]
    [InlineData(96u, 320, 320.0)]
    [InlineData(120u, 400, 320.0)]
    [InlineData(144u, 480, 320.0)]
    [InlineData(192u, 640, 320.0)]
    [InlineData(144u, 1, 0.6666666666666666)]
    public void Pixels_convert_to_dips_at_the_placement_scale(uint dpi, int pixels, double expected)
    {
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), dpi);

        Assert.Equal(expected, placement.PixelsToDip(pixels), 12);
    }

    [Fact]
    public void Pixel_to_dip_divides_rather_than_multiplies()
    {
        // Kills a reversed conversion. At 96 DPI both directions give the same
        // number, so only a non-unity scale can tell them apart.
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 2880, 1620), 144);

        Assert.Equal(1920.0, placement.PixelsToDip(2880));
        Assert.NotEqual(4320.0, placement.PixelsToDip(2880));
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(168u)]
    public void Pixels_round_trip_through_dips_exactly(uint dpi)
    {
        // PixelsToDip is not rounded, so converting back recovers the pixel
        // count at every scale, including fractional ones.
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), dpi);

        foreach (int px in new[] { 0, 1, 7, 360, 1032, 1920, 2880 })
        {
            Assert.Equal(px, placement.DipToPixels(placement.PixelsToDip(px)));
        }
    }

    [Fact]
    public void A_placement_with_zero_dpi_cannot_convert_pixels()
    {
        var placement = new WindowPlacement("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), 0);

        Assert.Throws<InvalidOperationException>(() => placement.PixelsToDip(100));
    }

    // ------------------------------------------------------ MonitorId

    [Fact]
    public void A_monitor_id_compares_by_value()
    {
        Assert.Equal(MonitorId.From("\\\\?\\DISPLAY#ABC#1"), MonitorId.From("\\\\?\\DISPLAY#ABC#1"));
        Assert.NotEqual(MonitorId.From("\\\\?\\DISPLAY#ABC#1"), MonitorId.From("\\\\?\\DISPLAY#ABC#2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_monitor_id_is_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => MonitorId.From(value));
    }

    [Fact]
    public void A_monitor_id_describes_itself_as_its_value()
    {
        // The value is what the per-monitor settings key is composed from.
        Assert.Equal("\\\\?\\DISPLAY#ABC#1", MonitorId.From("\\\\?\\DISPLAY#ABC#1").ToString());
    }

    // ------------------------------------------------- DisplayMonitor

    [Fact]
    public void A_display_keeps_bounds_and_work_area_apart()
    {
        DisplayMonitor display = new(
            MonitorId.From("id"), "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96, isPrimary: true);

        Assert.Equal(1080, display.Bounds.Height);
        Assert.Equal(1032, display.WorkArea.Height);
        Assert.Equal(display.WorkArea, display.WorkAreaPlacement.Bounds);
        Assert.Equal(96u, display.WorkAreaPlacement.Dpi);
    }

    [Fact]
    public void A_display_with_zero_dpi_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayMonitor(
            MonitorId.From("id"), "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 0, isPrimary: false));
    }

    [Theory]
    [InlineData(0, 0, 0, 1032)]    // zero width
    [InlineData(0, 0, 1920, 0)]    // zero height
    [InlineData(100, 0, 50, 1032)] // inverted
    public void A_display_with_an_empty_work_area_is_rejected(int l, int t, int r, int b)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayMonitor(
            MonitorId.From("id"), "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 1920, 1080), new PixelRect(l, t, r, b), 96, isPrimary: false));
    }

    [Fact]
    public void A_display_accepts_a_negative_origin()
    {
        DisplayMonitor display = new(
            MonitorId.From("id"), "\\\\.\\DISPLAY5",
            new PixelRect(-1920, 78, 0, 1158), new PixelRect(-1920, 78, 0, 1110), 96, isPrimary: false);

        Assert.Equal(1920, display.WorkArea.Width);
        Assert.Equal(-1920, display.WorkArea.Left);
    }
}
