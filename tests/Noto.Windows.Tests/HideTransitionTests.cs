using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Slice 1.3: a hide in motion. The save is the gate, the window is hidden only once the content has left, a show
/// reverses it, and no sequence of requests leaves it stuck in Hiding.
/// </summary>
public sealed class HideTransitionTests
{
    private int _saves;

    private bool SaveOk()
    {
        _saves++;
        return true;
    }

    private bool SaveFails()
    {
        _saves++;
        return false;
    }

    [Fact]
    public void A_hide_waits_for_a_successful_save_and_then_animates_without_hiding_yet()
    {
        var hide = new HideTransition();

        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));
        Assert.Equal(1, _saves);
        Assert.True(hide.Hiding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_failed_save_hides_nothing_and_starts_no_motion(bool animate)
    {
        var hide = new HideTransition();

        Assert.Equal(HideStep.Stay, hide.Begin(SaveFails, animate));
        Assert.False(hide.Hiding);
    }

    [Fact]
    public void With_animations_off_the_window_hides_at_once_and_nothing_is_left_hiding()
    {
        var hide = new HideTransition();

        Assert.Equal(HideStep.HideNow, hide.Begin(SaveOk, animate: false));
        Assert.False(hide.Hiding);
    }

    [Fact]
    public void The_window_is_hidden_only_when_the_exit_completes_and_text_typed_meanwhile_is_saved()
    {
        var hide = new HideTransition();
        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));

        Assert.Equal(HideStep.HideNow, hide.Complete(hide.Generation, SaveOk));
        Assert.Equal(2, _saves);      // once to start, once when the content has left
        Assert.False(hide.Hiding);
    }

    [Fact]
    public void Text_that_cannot_be_saved_when_the_exit_completes_brings_the_content_back()
    {
        var hide = new HideTransition();
        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));

        Assert.Equal(HideStep.Restore, hide.Complete(hide.Generation, SaveFails));
        Assert.False(hide.Hiding);
    }

    [Fact]
    public void A_show_during_the_hide_reverses_it_and_the_late_completion_hides_nothing()
    {
        var hide = new HideTransition();
        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));
        int generation = hide.Generation;

        Assert.True(hide.Reverse());
        Assert.False(hide.Hiding);

        // The replaced animation still completes its batch: that completion must not hide the window.
        Assert.Equal(HideStep.Stay, hide.Complete(generation, SaveOk));
        Assert.Equal(1, _saves);
    }

    [Fact]
    public void A_reversal_with_nothing_hiding_changes_nothing()
    {
        var hide = new HideTransition();

        Assert.False(hide.Reverse());
        Assert.False(hide.Hiding);
    }

    [Fact]
    public void A_second_hide_while_hiding_is_ignored_without_saving_again()
    {
        var hide = new HideTransition();
        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));

        Assert.Equal(HideStep.Stay, hide.Begin(SaveOk, animate: true));
        Assert.Equal(1, _saves);
        Assert.True(hide.Hiding);
    }

    [Fact]
    public void Repeated_hides_and_shows_are_deterministic_and_only_the_latest_completion_counts()
    {
        var hide = new HideTransition();

        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));
        int first = hide.Generation;
        Assert.True(hide.Reverse());
        Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));
        int second = hide.Generation;

        Assert.NotEqual(first, second);
        Assert.Equal(HideStep.Stay, hide.Complete(first, SaveOk));     // the first, reversed hide's late completion
        Assert.True(hide.Hiding);                                      // the second is still running
        Assert.Equal(HideStep.HideNow, hide.Complete(second, SaveOk));
        Assert.False(hide.Hiding);
        Assert.Equal(HideStep.Stay, hide.Complete(second, SaveOk));    // a duplicate completion hides nothing twice
    }

    [Fact]
    public void No_sequence_leaves_it_stuck_in_hiding()
    {
        // Every way out of Hiding: completion (hidden or restored) and reversal.
        foreach (Func<HideTransition, HideStep> end in new Func<HideTransition, HideStep>[]
        {
            h => h.Complete(h.Generation, SaveOk),
            h => h.Complete(h.Generation, SaveFails),
            h => h.Reverse() ? HideStep.Stay : HideStep.Stay,
        })
        {
            var hide = new HideTransition();
            Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));

            _ = end(hide);

            Assert.False(hide.Hiding);
            Assert.Equal(HideStep.Animate, hide.Begin(SaveOk, animate: true));   // and a new hide can start
        }
    }
}

/// <summary>Slice 1.3: the direction and values of every motion, tested without the compositor.</summary>
public sealed class MotionPlanTests
{
    [Fact]
    public void The_drawer_enters_from_and_leaves_toward_its_docked_edge()
    {
        // Right edge: the content starts 24 DIP to the right (toward the edge) and leaves 16 DIP to the right.
        Assert.Equal(24, MotionPlan.EnterFrom(DockEdge.Right, 24));
        Assert.Equal(-24, MotionPlan.EnterFrom(DockEdge.Left, 24));
        Assert.Equal(16, MotionPlan.ExitTo(DockEdge.Right, 16));
        Assert.Equal(-16, MotionPlan.ExitTo(DockEdge.Left, 16));
    }

    [Fact]
    public void A_surface_enters_from_the_right_going_forward_and_from_the_left_going_back()
    {
        Assert.Equal(16, MotionPlan.SurfaceFrom(forward: true, 16));
        Assert.Equal(-16, MotionPlan.SurfaceFrom(forward: false, 16));
    }

    [Fact]
    public void Animation_effects_off_means_no_transition()
    {
        Assert.False(MotionPlan.Animate(animationsEnabled: false));
        Assert.True(MotionPlan.Animate(animationsEnabled: true));
    }

    [Fact]
    public void WinUIs_resources_are_read_as_they_are_written()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), MotionPlan.ParseDuration("00:00:00.250"));
        Assert.Equal(TimeSpan.FromMilliseconds(167), MotionPlan.ParseDuration("00:00:00.167"));
        Assert.Equal((0f, 0f, 0f, 1f), MotionPlan.ParseSpline("0,0,0,1"));
        Assert.Throws<FormatException>(() => MotionPlan.ParseDuration("00:00:00"));
        Assert.Throws<FormatException>(() => MotionPlan.ParseSpline("0,0,1"));
    }
}
