using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The show/hide toggle's rules (#16 slice 5, ADR-007 §4), with no window.
/// </summary>
public sealed class WorkspaceToggleTests
{
    private const int Ready = 10_000;

    private static ActivationContext After(int requestTime, int? lastTransitionEnd = null) =>
        new(ShuttingDown: false, Resizing: false, Transitioning: false, requestTime, Ready, lastTransitionEnd);

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.Hide)]
    public void The_toggle_after_startup(WorkspacePresence presence, WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceToggle.Decide(presence, After(Ready + 500)));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void Nothing_happens_while_closing_resizing_or_mid_transition(WorkspacePresence presence)
    {
        ActivationContext ok = After(Ready + 500);

        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(presence, ok with { ShuttingDown = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(presence, ok with { Resizing = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(presence, ok with { Transitioning = true }));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.None)]
    public void A_press_made_during_startup_shows_but_never_hides(WorkspacePresence presence, WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceToggle.Decide(presence, After(Ready - 300)));
    }

    [Fact]
    public void A_press_made_before_the_last_transition_ended_is_stale()
    {
        // Queued behind a hide that took 200 ms: replaying it would show the
        // window straight back.
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspacePresence.Hidden, After(Ready + 900, lastTransitionEnd: Ready + 1000)));
    }

    [Fact]
    public void A_press_made_after_the_last_transition_ended_is_handled()
    {
        Assert.Equal(WorkspaceAction.Show, WorkspaceToggle.Decide(WorkspacePresence.Hidden, After(Ready + 1001, lastTransitionEnd: Ready + 1000)));
    }

    [Fact]
    public void A_startup_burst_is_coalesced_into_one_bring_forward()
    {
        // Three presses queued while Noto was starting, delivered together
        // after OnLaunched returned. The first acts; the transition it ran
        // ends after all three were made, so the rest are stale.
        int[] burst = [Ready - 400, Ready - 250, Ready - 100];
        var actions = new List<WorkspaceAction>();
        int? lastEnd = null;
        WorkspacePresence presence = WorkspacePresence.Background;

        foreach (int press in burst)
        {
            WorkspaceAction action = WorkspaceToggle.Decide(presence, After(press, lastEnd));
            actions.Add(action);

            if (action != WorkspaceAction.None)
            {
                presence = WorkspacePresence.Foreground;
                lastEnd = Ready + 20;
            }
        }

        Assert.Equal([WorkspaceAction.Focus, WorkspaceAction.None, WorkspaceAction.None], actions);
    }

    [Theory]
    [InlineData(5, 6, true)]
    [InlineData(6, 5, false)]
    [InlineData(5, 5, false)]
    [InlineData(int.MaxValue, int.MinValue, true)] // the clock wrapped between the two
    [InlineData(int.MinValue, int.MaxValue, false)]
    public void Tick_order_survives_the_clock_wrapping(int time, int reference, bool before)
    {
        Assert.Equal(before, WorkspaceToggle.IsBefore(time, reference));
    }
}
