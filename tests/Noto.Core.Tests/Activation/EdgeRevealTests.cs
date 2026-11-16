using Noto.Core.Activation;
using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Activation;

/// <summary>
/// Hot Side and drag to reveal (#185; parity A4, A15): the rules the low-level hook feeds, without a pointer. What the
/// running application does with a real pointer is Invoke-Settings.ps1 case 23's to prove.
/// </summary>
public sealed class EdgeRevealTests
{
    // A 1920x1080 display, the work area's span less its corners live, docked right; nothing beyond the edge.
    private static readonly EdgeZone RightEdge = new(WorkspaceEdge.Right, 0, 1920, 48, 992, Shared: false);
    private static readonly EdgeRevealOptions Both = new(HotSide: true, Drag: true, RightEdge, DragSlopX: 4, DragSlopY: 4);
    private static readonly RevealContext Clear = new(Shown: false, Immersive: false, Locked: false, Moving: false);

    [Fact]
    public void A_pointer_resting_at_the_edge_reveals_after_the_dwell()
    {
        var reveal = new EdgeReveal();

        Assert.Equal(EdgeStep.None, reveal.Moved(1500, 500, PointerButtons.None, 1000, Both));
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1919, 500, PointerButtons.None, 1100, Both));
        Assert.Equal(EdgeGesture.Rest, reveal.Armed);
        Assert.Equal(EdgeReveal.RestDwell, reveal.Dwell);

        Assert.Equal(new EdgeDecision(EdgeGesture.Rest, EdgeOutcome.Reveal), reveal.Elapsed(1100 + EdgeReveal.RestDwell, Both, Clear));
        Assert.Equal(EdgeGesture.None, reveal.Armed);
    }

    [Theory]
    [InlineData(1918, true)]   // the band is the outermost two columns
    [InlineData(1919, true)]
    [InlineData(1917, false)]
    [InlineData(1925, true)]   // past the edge, as the hook reports a pointer pushed against it
    public void Only_the_outermost_columns_are_the_edge(int x, bool armed)
    {
        Assert.Equal(armed ? EdgeStep.Arm : EdgeStep.None, new EdgeReveal().Moved(x, 500, PointerButtons.None, 0, Both));
    }

    [Fact]
    public void Jitter_within_a_few_pixels_keeps_resting_and_more_restarts_the_dwell()
    {
        var reveal = new EdgeReveal();
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1919, 500, PointerButtons.None, 1000, Both));

        // Pushing past the edge and a pixel or three up and down is still resting.
        Assert.Equal(EdgeStep.None, reveal.Moved(1930, 503, PointerButtons.None, 1100, Both));
        Assert.Equal(EdgeStep.None, reveal.Moved(1918, 497, PointerButtons.None, 1200, Both));

        // Sliding along the edge is not: the dwell starts again, and the old due time no longer reveals.
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1919, 520, PointerButtons.None, 1250, Both));
        Assert.Equal(EdgeOutcome.None, reveal.Elapsed(1300, Both, Clear).Outcome);
        Assert.Equal(EdgeOutcome.Reveal, reveal.Elapsed(1250 + EdgeReveal.RestDwell, Both, Clear).Outcome);
    }

    [Fact]
    public void Leaving_the_edge_disarms()
    {
        var reveal = new EdgeReveal();
        reveal.Moved(1919, 500, PointerButtons.None, 0, Both);

        Assert.Equal(EdgeStep.Disarm, reveal.Moved(1800, 500, PointerButtons.None, 100, Both));
        Assert.Equal(EdgeOutcome.None, reveal.Elapsed(EdgeReveal.RestDwell, Both, Clear).Outcome);
    }

    [Theory]
    [InlineData(PointerButtons.Left)]
    [InlineData(PointerButtons.Right)]
    [InlineData(PointerButtons.Middle)]
    [InlineData(PointerButtons.Extra)]
    public void Hot_side_never_arms_with_a_button_held_and_a_press_disarms_it(PointerButtons button)
    {
        var reveal = new EdgeReveal();
        Assert.Equal(EdgeStep.None, reveal.Moved(1919, 500, button, 0, Both));

        var resting = new EdgeReveal();
        resting.Moved(1919, 500, PointerButtons.None, 0, Both);
        Assert.Equal(EdgeStep.Disarm, resting.Moved(1919, 500, button, 50, Both));
    }

    [Fact]
    public void A_drag_reaching_the_edge_reveals_after_the_shorter_dwell_even_moving_along_it()
    {
        var reveal = new EdgeReveal();

        reveal.Moved(1000, 500, PointerButtons.Left, 0, Both);
        Assert.Equal(EdgeStep.None, reveal.Moved(1200, 500, PointerButtons.Left, 50, Both));
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1919, 500, PointerButtons.Left, 100, Both));
        Assert.Equal(EdgeGesture.Drag, reveal.Armed);
        Assert.Equal(EdgeReveal.DragDwell, reveal.Dwell);

        Assert.Equal(EdgeStep.None, reveal.Moved(1919, 600, PointerButtons.Left, 150, Both));
        Assert.Equal(new EdgeDecision(EdgeGesture.Drag, EdgeOutcome.Reveal), reveal.Elapsed(100 + EdgeReveal.DragDwell, Both, Clear));
    }

    [Fact]
    public void A_press_that_moves_less_than_the_drag_threshold_is_no_drag()
    {
        // Pressed at the edge and held still (a scroll bar, a click about to happen): neither a drag nor a rest.
        var reveal = new EdgeReveal();

        Assert.Equal(EdgeStep.None, reveal.Moved(1919, 500, PointerButtons.Left, 0, Both));
        Assert.Equal(EdgeStep.None, reveal.Moved(1918, 503, PointerButtons.Left, 50, Both));
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1918, 510, PointerButtons.Left, 60, Both));
    }

    [Fact]
    public void Releasing_the_drag_at_the_edge_disarms_it_and_then_rests()
    {
        var reveal = new EdgeReveal();
        reveal.Moved(1000, 500, PointerButtons.Left, 0, Both);
        reveal.Moved(1919, 500, PointerButtons.Left, 50, Both);
        Assert.Equal(EdgeGesture.Drag, reveal.Armed);

        // The drop: the drag is over; resting there afterwards is Hot Side's, with its own dwell.
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1919, 500, PointerButtons.None, 100, Both));
        Assert.Equal(EdgeGesture.Rest, reveal.Armed);
    }

    [Fact]
    public void Each_setting_governs_only_its_own_gesture()
    {
        EdgeRevealOptions hotSideOnly = Both with { Drag = false };
        EdgeRevealOptions dragOnly = Both with { HotSide = false };

        Assert.Equal(EdgeStep.Arm, new EdgeReveal().Moved(1919, 500, PointerButtons.None, 0, hotSideOnly));
        Assert.Equal(EdgeStep.None, new EdgeReveal().Moved(1919, 500, PointerButtons.None, 0, dragOnly));

        var drag = new EdgeReveal();
        drag.Moved(1000, 500, PointerButtons.Left, 0, hotSideOnly);
        Assert.Equal(EdgeStep.None, drag.Moved(1919, 500, PointerButtons.Left, 50, hotSideOnly));

        drag = new EdgeReveal();
        drag.Moved(1000, 500, PointerButtons.Left, 0, dragOnly);
        Assert.Equal(EdgeStep.Arm, drag.Moved(1919, 500, PointerButtons.Left, 50, dragOnly));
    }

    [Fact]
    public void A_setting_turned_off_while_armed_reveals_nothing()
    {
        var reveal = new EdgeReveal();
        reveal.Moved(1919, 500, PointerButtons.None, 0, Both);

        Assert.Equal(EdgeOutcome.None, reveal.Elapsed(EdgeReveal.RestDwell, Both with { HotSide = false }, Clear).Outcome);
        Assert.Equal(EdgeOutcome.None, new EdgeReveal().Elapsed(EdgeReveal.RestDwell, Both, Clear).Outcome);
    }

    [Theory]
    [InlineData(true, false, false, false, EdgeOutcome.Shown)]
    [InlineData(false, true, false, false, EdgeOutcome.Immersive)]
    [InlineData(false, false, true, false, EdgeOutcome.Locked)]
    [InlineData(false, false, false, true, EdgeOutcome.Moving)]
    [InlineData(true, true, true, true, EdgeOutcome.Locked)]
    public void Already_shown_full_screen_locked_or_snapping_never_reveals(bool shown, bool immersive, bool locked, bool moving, EdgeOutcome outcome)
    {
        foreach (PointerButtons held in new[] { PointerButtons.None, PointerButtons.Left })
        {
            var reveal = new EdgeReveal();
            reveal.Moved(1000, 500, held, 0, Both);
            reveal.Moved(1919, 500, held, 50, Both);

            Assert.Equal(outcome, reveal.Elapsed(1000, Both, new RevealContext(shown, immersive, locked, moving)).Outcome);
        }
    }

    [Fact]
    public void Once_per_arrival_the_pointer_must_leave_the_edge_to_try_again()
    {
        var reveal = new EdgeReveal();
        reveal.Moved(1919, 500, PointerButtons.None, 0, Both);
        Assert.Equal(EdgeOutcome.Reveal, reveal.Elapsed(EdgeReveal.RestDwell, Both, Clear).Outcome);

        // Still at the edge, even sliding along it: spent, whatever happened to Noto meanwhile.
        Assert.Equal(EdgeStep.None, reveal.Moved(1919, 700, PointerButtons.None, 400, Both));
        Assert.Equal(EdgeGesture.None, reveal.Armed);

        // Away and back: a new arrival.
        Assert.Equal(EdgeStep.None, reveal.Moved(1700, 700, PointerButtons.None, 500, Both));
        Assert.Equal(EdgeStep.Arm, reveal.Moved(1919, 700, PointerButtons.None, 600, Both));
    }

    [Theory]
    [InlineData(1919, 47)]    // the corner above the live span (a maximized window's close button)
    [InlineData(1919, 992)]   // below it (the taskbar)
    [InlineData(1919, -5)]
    [InlineData(-1, 500)]     // the other edge
    [InlineData(0, 500)]
    public void Outside_the_live_span_or_at_the_other_edge_is_not_the_edge(int x, int y)
    {
        Assert.Equal(EdgeStep.None, new EdgeReveal().Moved(x, y, PointerButtons.None, 0, Both));
    }

    [Fact]
    public void Past_an_edge_another_display_shares_is_that_display()
    {
        EdgeRevealOptions shared = Both with { Zone = RightEdge with { Shared = true } };

        Assert.Equal(EdgeStep.None, new EdgeReveal().Moved(1920, 500, PointerButtons.None, 0, shared));
        Assert.Equal(EdgeStep.None, new EdgeReveal().Moved(2500, 500, PointerButtons.None, 0, shared));
        Assert.Equal(EdgeStep.Arm, new EdgeReveal().Moved(1919, 500, PointerButtons.None, 0, shared));
    }

    [Fact]
    public void The_left_edge_mirrors_the_right_and_a_display_left_of_the_primary_is_negative()
    {
        EdgeRevealOptions left = Both with { Zone = new EdgeZone(WorkspaceEdge.Left, -1920, 0, 0, 1040, Shared: false) };

        Assert.Equal(EdgeStep.Arm, new EdgeReveal().Moved(-1920, 500, PointerButtons.None, 0, left));
        Assert.Equal(EdgeStep.Arm, new EdgeReveal().Moved(-1919, 500, PointerButtons.None, 0, left));
        Assert.Equal(EdgeStep.Arm, new EdgeReveal().Moved(-1930, 500, PointerButtons.None, 0, left));
        Assert.Equal(EdgeStep.None, new EdgeReveal().Moved(-1918, 500, PointerButtons.None, 0, left));
        Assert.Equal(EdgeStep.None, new EdgeReveal().Moved(-1, 500, PointerButtons.None, 0, left));
    }

    [Fact]
    public void With_no_known_edge_nothing_arms_and_an_armed_gesture_is_dropped()
    {
        var reveal = new EdgeReveal();
        Assert.Equal(EdgeStep.None, reveal.Moved(1919, 500, PointerButtons.None, 0, null));

        reveal.Moved(1919, 500, PointerButtons.None, 0, Both);
        Assert.Equal(EdgeStep.Disarm, reveal.Moved(1919, 500, PointerButtons.None, 10, null));
    }

    [Fact]
    public void A_timer_firing_a_little_early_counts_and_the_tick_clock_may_wrap()
    {
        var reveal = new EdgeReveal();
        int start = int.MaxValue - 100;
        reveal.Moved(1919, 500, PointerButtons.None, start, Both);

        Assert.Equal(EdgeOutcome.Reveal, reveal.Elapsed(unchecked(start + EdgeReveal.RestDwell - EdgeReveal.TimerSlack), Both, Clear).Outcome);
    }
}
