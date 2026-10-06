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
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, After(Ready + 500)));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void Nothing_happens_while_closing_resizing_or_mid_transition(WorkspacePresence presence)
    {
        ActivationContext ok = After(Ready + 500);

        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, ok with { ShuttingDown = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, ok with { Resizing = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, ok with { Transitioning = true }));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.None)]
    public void A_press_made_during_startup_shows_but_never_hides(WorkspacePresence presence, WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, After(Ready - 300)));
    }

    [Fact]
    public void A_press_made_before_the_last_transition_ended_is_stale()
    {
        // Queued behind a hide that took 200 ms: replaying it would show the
        // window straight back.
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, WorkspacePresence.Hidden, After(Ready + 900, lastTransitionEnd: Ready + 1000)));
    }

    [Fact]
    public void A_press_made_after_the_last_transition_ended_is_handled()
    {
        Assert.Equal(WorkspaceAction.Show, WorkspaceToggle.Decide(WorkspaceRequest.Toggle, WorkspacePresence.Hidden, After(Ready + 1001, lastTransitionEnd: Ready + 1000)));
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
            WorkspaceAction action = WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, After(press, lastEnd));
            actions.Add(action);

            if (action != WorkspaceAction.None)
            {
                presence = WorkspacePresence.Foreground;
                lastEnd = Ready + 20;
            }
        }

        Assert.Equal([WorkspaceAction.Focus, WorkspaceAction.None, WorkspaceAction.None], actions);
    }

    // ------------------------------------------------ a second launch (A17)

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.None)]
    public void A_launch_shows_restores_or_brings_forward_and_never_hides(WorkspacePresence presence, WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, After(Ready + 500)));
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, After(Ready - 300)));
    }

    [Fact]
    public void Only_the_launch_differs_from_the_toggle_and_only_when_in_front()
    {
        foreach (WorkspacePresence presence in Enum.GetValues<WorkspacePresence>())
        {
            WorkspaceAction toggle = WorkspaceToggle.Decide(WorkspaceRequest.Toggle, presence, After(Ready + 500));
            WorkspaceAction launch = WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, After(Ready + 500));

            Assert.Equal(presence == WorkspacePresence.Foreground ? WorkspaceAction.None : toggle, launch);
        }
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Background)]
    public void A_launch_is_dropped_by_the_same_rules_as_the_toggle(WorkspacePresence presence)
    {
        ActivationContext ok = After(Ready + 500);

        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, ok with { ShuttingDown = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, ok with { Resizing = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, ok with { Transitioning = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, After(Ready + 900, lastTransitionEnd: Ready + 1000)));
    }

    // ---------------------------------------- putting it away (slice 6)

    private static ActivationContext Ready_(bool hideOnDeactivation, bool current = true) =>
        After(Ready + 500) with { HideOnDeactivation = hideOnDeactivation, DeactivationCurrent = current };

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.None)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.None)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Hide)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.Hide)]
    public void A_dismissal_hides_a_shown_window_and_never_shows_one(WorkspacePresence presence, WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Dismiss, presence, Ready_(hideOnDeactivation: false)));
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Dismiss, presence, Ready_(hideOnDeactivation: true)));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.None)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.None)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Hide)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.Hide)]
    public void A_current_deactivation_hides_a_shown_window(WorkspacePresence presence, WorkspaceAction expected)
    {
        // Foreground: Windows reports that activation is leaving before the
        // foreground has visibly changed — the shell-launch race (PR #92).
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Deactivated, presence, Ready_(hideOnDeactivation: true)));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void A_stale_deactivation_does_nothing(WorkspacePresence presence)
    {
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Deactivated, presence, Ready_(hideOnDeactivation: true, current: false)));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void A_deactivation_does_nothing_when_pinned_or_switched_off(WorkspacePresence presence)
    {
        // HideOnDeactivation is false when the setting is off or the
        // workspace is pinned: the coordinator passes setting && !pinned.
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Deactivated, presence, Ready_(hideOnDeactivation: false)));
    }

    [Theory]
    [InlineData(WorkspaceRequest.Dismiss, WorkspacePresence.Foreground)]
    [InlineData(WorkspaceRequest.Deactivated, WorkspacePresence.Background)]
    public void Nothing_is_put_away_during_startup(WorkspaceRequest request, WorkspacePresence presence)
    {
        ActivationContext starting = After(Ready - 300) with { HideOnDeactivation = true, DeactivationCurrent = true };

        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(request, presence, starting));
    }

    [Theory]
    [InlineData(WorkspaceRequest.Dismiss, WorkspacePresence.Foreground)]
    [InlineData(WorkspaceRequest.Deactivated, WorkspacePresence.Background)]
    public void Putting_it_away_follows_the_same_drop_rules(WorkspaceRequest request, WorkspacePresence presence)
    {
        ActivationContext ok = Ready_(hideOnDeactivation: true);

        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(request, presence, ok with { ShuttingDown = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(request, presence, ok with { Resizing = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(request, presence, ok with { Transitioning = true }));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(request, presence, After(Ready + 900, lastTransitionEnd: Ready + 1000) with { HideOnDeactivation = true, DeactivationCurrent = true }));
    }

    [Theory]
    [InlineData(WorkspaceRequest.Dismiss)]
    [InlineData(WorkspaceRequest.Deactivated)]
    public void Putting_it_away_never_shows_restores_or_focuses(WorkspaceRequest request)
    {
        foreach (WorkspacePresence presence in Enum.GetValues<WorkspacePresence>())
        {
            foreach (bool allowed in new[] { true, false })
            {
                foreach (bool current in new[] { true, false })
                {
                    WorkspaceAction action = WorkspaceToggle.Decide(request, presence, Ready_(allowed, current));

                    Assert.Contains(action, new[] { WorkspaceAction.None, WorkspaceAction.Hide });
                }
            }
        }
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.None)]
    public void The_hide_on_deactivation_switch_changes_nothing_for_a_launch(WorkspacePresence presence, WorkspaceAction expected)
    {
        // A17's launch contract is untouched by slice 6.
        Assert.Equal(expected, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, Ready_(hideOnDeactivation: true)));
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
