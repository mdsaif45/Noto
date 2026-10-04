using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The position guard's decision for a restore from minimized, and for the
/// ordinary moves it must not mistake for one (#16 restore/re-dock fix).
/// </summary>
/// <remarks>
/// Deterministic: displays are constructed, so the development pair — a
/// second display left of the primary and 78px lower — is covered without
/// being connected. That pair is what made the old guard restore the window
/// off-screen: choosing a display by the minimized parking position picks
/// the one nearest to (-32000,-32000).
/// </remarks>
public sealed class DockedRestoreTests
{
    // Primary: (0,0)-(1920,1080), bottom taskbar -> work (0,0)-(1920,1032).
    private static readonly DisplayMonitor Primary = Display(
        "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96);

    // Left of primary, 78px lower: work (-1920,78)-(0,1110).
    private static readonly DisplayMonitor LeftOfPrimary = Display(
        "\\\\.\\DISPLAY5", new PixelRect(-1920, 78, 0, 1158), new PixelRect(-1920, 78, 0, 1110), 96);

    // 300 DIP wide: the workspace maximum here is below 600.
    private static readonly DisplayMonitor Narrow = Display(
        "\\\\.\\DISPLAY7", new PixelRect(0, 0, 300, 800), new PixelRect(0, 0, 300, 800), 96);

    private static readonly DisplayMonitor[] DevelopmentPair = [Primary, LeftOfPrimary];

    private static readonly FrameInset Measured = new(7, 0, 7, 7);

    // Where Windows parks a minimized window, and its visible frame there (measured: 146 px wide).
    private static readonly PixelRect Parking = new(-32000, -32000, -31840, -31972);
    private const int ParkedVisibleWidth = 146;

    // -------------------------------------------------------------- restore

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void A_restore_docks_at_the_remembered_width_not_the_minimized_frame(DockEdge edge)
    {
        PixelRect saved = DockGeometry.OuterBounds(Primary, edge, 360, Measured);

        PixelRect? target = DockedWindow.GuardTarget([Primary], edge, Parking, saved, ParkedVisibleWidth, 360, Measured);

        Assert.Equal(saved, target);
        Assert.Equal(360, Visible(target!.Value).Width);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void A_restore_lands_on_the_display_under_the_saved_placement(DockEdge edge)
    {
        PixelRect savedOnPrimary = DockGeometry.OuterBounds(Primary, edge, 360, Measured);
        PixelRect savedOnLeft = DockGeometry.OuterBounds(LeftOfPrimary, edge, 360, Measured);

        PixelRect? onPrimary = DockedWindow.GuardTarget(DevelopmentPair, edge, Parking, savedOnPrimary, ParkedVisibleWidth, 360, Measured);
        PixelRect? onLeft = DockedWindow.GuardTarget(DevelopmentPair, edge, Parking, savedOnLeft, ParkedVisibleWidth, 360, Measured);

        // The display nearest the parking position is LeftOfPrimary: a primary
        // restore landing there is the defect this replaces.
        Assert.Equal(savedOnPrimary, onPrimary);
        Assert.Equal(savedOnLeft, onLeft);
    }

    [Fact]
    public void With_no_remembered_width_a_restore_is_let_through_rather_than_guessed()
    {
        PixelRect saved = DockGeometry.OuterBounds(Primary, DockEdge.Right, 360, Measured);

        Assert.Null(DockedWindow.GuardTarget([Primary], DockEdge.Right, Parking, saved, ParkedVisibleWidth, null, Measured));
    }

    [Fact]
    public void With_no_placed_inset_a_restore_is_let_through_rather_than_measured_while_minimized()
    {
        PixelRect saved = DockGeometry.OuterBounds(Primary, DockEdge.Right, 360, Measured);

        Assert.Null(DockedWindow.GuardTarget([Primary], DockEdge.Right, Parking, saved, ParkedVisibleWidth, 360, null));
    }

    [Fact]
    public void Repeated_restore_proposals_give_the_same_answer()
    {
        PixelRect saved = DockGeometry.OuterBounds(Primary, DockEdge.Right, 360, Measured);

        PixelRect? first = DockedWindow.GuardTarget(DevelopmentPair, DockEdge.Right, Parking, saved, ParkedVisibleWidth, 360, Measured);
        PixelRect? second = DockedWindow.GuardTarget(DevelopmentPair, DockEdge.Right, Parking, saved, ParkedVisibleWidth, 360, Measured);

        Assert.Equal(first, second);
    }

    [Fact]
    public void An_interrupted_restore_leaves_nothing_behind_for_the_next_one()
    {
        PixelRect saved = DockGeometry.OuterBounds(Primary, DockEdge.Right, 360, Measured);

        // Proposed and current both off every display: nothing to dock by.
        Assert.Null(DockedWindow.GuardTarget(DevelopmentPair, DockEdge.Right, Parking, Parking, ParkedVisibleWidth, 360, Measured));

        // The decision keeps no state, so the next restore docks normally.
        Assert.Equal(saved, DockedWindow.GuardTarget(DevelopmentPair, DockEdge.Right, Parking, saved, ParkedVisibleWidth, 360, Measured));
    }

    // --------------------------------------------- ordinary moves are not restores

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void An_ordinary_move_is_judged_by_where_the_window_is_not_where_it_is_sent(DockEdge edge)
    {
        PixelRect docked = DockGeometry.OuterBounds(Primary, edge, 360, Measured);
        var towardsLeftDisplay = new PixelRect(-1500, 200, -1100, 900);   // e.g. Win+Shift+Left

        PixelRect? target = DockedWindow.GuardTarget(DevelopmentPair, edge, docked, towardsLeftDisplay, 360, 360, Measured);

        Assert.Equal(docked, target);
    }

    [Fact]
    public void Without_a_remembered_width_an_ordinary_move_keeps_the_current_visible_width()
    {
        PixelRect docked = DockGeometry.OuterBounds(Primary, DockEdge.Right, 500, Measured);

        PixelRect? target = DockedWindow.GuardTarget([Primary], DockEdge.Right, docked, new PixelRect(100, 100, 900, 700), 500, null, Measured);

        Assert.Equal(docked, target);
    }

    // ---------------------------------------------- remembered vs effective width

    [Fact]
    public void A_narrow_display_clamps_the_effective_width_only()
    {
        PixelRect onNarrow = DockGeometry.OuterBounds(Narrow, DockEdge.Right, 600, Measured);

        PixelRect? clamped = DockedWindow.GuardTarget([Narrow], DockEdge.Right, onNarrow, new PixelRect(10, 10, 200, 200), 280, 600, Measured);
        PixelRect? wide = DockedWindow.GuardTarget([Primary], DockEdge.Right, Parking, DockGeometry.OuterBounds(Primary, DockEdge.Right, 360, Measured), 0, 600, Measured);

        Assert.True(Visible(clamped!.Value).Width < 600);
        Assert.Equal(600, Visible(wide!.Value).Width);
    }

    // ----------------------------------------------------- what is let through

    [Theory]
    [InlineData(false, false, 0x8174u, true, true)]    // becoming minimized: Windows places it
    [InlineData(false, false, 0x8120u, false, false)]  // a restore or any move: guarded
    [InlineData(true, false, 0u, false, true)]         // Noto's own move
    [InlineData(false, true, 0u, false, true)]         // a resize step
    [InlineData(false, false, 0x0003u, false, true)]   // neither moves nor sizes
    public void What_passes_the_guard_unchanged(bool ownMove, bool inSizeLoop, uint flags, bool minimized, bool expected)
    {
        Assert.Equal(expected, DockedWindow.LetsThrough(ownMove, inSizeLoop, flags, minimized));
    }

    [Fact]
    public void The_parking_position_and_an_adjacent_rectangle_are_on_no_display()
    {
        Assert.False(DockedWindow.IsOnAnyDisplay(Parking, DevelopmentPair));
        Assert.False(DockedWindow.IsOnAnyDisplay(new PixelRect(1920, 0, 2000, 100), [Primary]));
        Assert.True(DockedWindow.IsOnAnyDisplay(new PixelRect(1919, 0, 2000, 100), [Primary]));
    }

    // ------------------------------------------------------------- helpers

    private static PixelRect Visible(PixelRect outer) => new(
        outer.Left + Measured.Left, outer.Top + Measured.Top, outer.Right - Measured.Right, outer.Bottom - Measured.Bottom);

    private static DisplayMonitor Display(string device, PixelRect bounds, PixelRect work, uint dpi) =>
        new(MonitorId.From(device + "#id"), device, bounds, work, dpi, isPrimary: false);
}
