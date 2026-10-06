using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The activation generation (#16 slice 6, ADR-007 §4 "The drawer"): a
/// deactivation hides only while nothing has activated or brought the
/// workspace forward since it was made — decided by event order, never time.
/// </summary>
/// <remarks>
/// Each test plays one ordering of <c>WM_ACTIVATEAPP</c> and requests through
/// the real <see cref="ActivationGeneration"/>, <see cref="WorkspaceToggle.Decide"/>
/// and <see cref="WorkspaceToggle.BringsForward"/>, the way the coordinator
/// does on the UI thread. The coordinator's own wiring is held by
/// <see cref="WorkspaceDrawerGuardTests"/>; the real activation changes by the
/// runtime harness.
/// </remarks>
public sealed class ActivationGenerationTests
{
    private const int Ready = 10_000;

    /// <summary>The coordinator's decision path, with the window reduced to its presence.</summary>
    private sealed class Drawer(WorkspacePresence presence)
    {
        private readonly ActivationGeneration _generation = new();

        public WorkspacePresence Presence { get; private set; } = presence;

        public bool Pinned { get; set; }

        public bool Setting { get; set; } = true;

        public bool ShuttingDown { get; set; }

        public bool Resizing { get; set; }

        public int RequestTime { get; set; } = Ready + 500;

        /// <summary><c>WM_ACTIVATEAPP(FALSE)</c>: returns the generation the posted request carries.</summary>
        public int Leave() => _generation.Deactivated();

        /// <summary><c>WM_ACTIVATEAPP(TRUE)</c>: returns the generation the posted request carries.</summary>
        public int Return() => _generation.Activated();

        public WorkspaceAction Deactivated(int generation) => Run(WorkspaceRequest.Deactivated, _generation.IsCurrent(generation));

        public WorkspaceAction Activated(int generation) => Run(WorkspaceRequest.Activated, _generation.IsCurrent(generation));

        public WorkspaceAction Request(WorkspaceRequest request) => Run(request, deactivationCurrent: false);

        private WorkspaceAction Run(WorkspaceRequest request, bool deactivationCurrent)
        {
            WorkspaceAction action = WorkspaceToggle.Decide(
                request,
                Presence,
                new ActivationContext(ShuttingDown, Resizing, Transitioning: false, RequestTime, Ready, LastTransitionEnd: null,
                    HideOnDeactivation: Setting && !Pinned, GenerationCurrent: deactivationCurrent));

            if (WorkspaceToggle.BringsForward(action))
            {
                _generation.BroughtForward();
            }

            Presence = action switch
            {
                WorkspaceAction.Hide => WorkspacePresence.Hidden,
                WorkspaceAction.Show or WorkspaceAction.Restore or WorkspaceAction.Focus => WorkspacePresence.Foreground,
                _ => Presence,
            };

            return action;
        }
    }

    [Theory]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)] // the shell-launch race: still reads "in front"
    public void A_deactivation_that_still_holds_hides(WorkspacePresence presence)
    {
        var drawer = new Drawer(presence);
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.Hide, drawer.Deactivated(n));
        Assert.Equal(WorkspacePresence.Hidden, drawer.Presence);
    }

    [Theory]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void A_deactivation_is_stale_once_activation_returned(WorkspacePresence presence)
    {
        var drawer = new Drawer(presence);
        int n = drawer.Leave();
        _ = drawer.Return();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
        Assert.Equal(presence, drawer.Presence);
    }

    [Fact]
    public void Two_leave_and_return_cycles_leave_both_requests_stale()
    {
        var drawer = new Drawer(WorkspacePresence.Foreground);
        int first = drawer.Leave();
        _ = drawer.Return();
        int second = drawer.Leave();
        _ = drawer.Return();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(first));
        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(second));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Fact]
    public void After_leave_return_leave_only_the_second_deactivation_hides()
    {
        var drawer = new Drawer(WorkspacePresence.Foreground);
        int first = drawer.Leave();
        _ = drawer.Return();
        int second = drawer.Leave();

        // The first request, decided during the second deactivation, must not act for it.
        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(first));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);

        Assert.Equal(WorkspaceAction.Hide, drawer.Deactivated(second));
    }

    [Fact]
    public void Every_change_is_a_new_generation()
    {
        var generation = new ActivationGeneration();
        int a = generation.Deactivated();
        _ = generation.Activated();
        int b = generation.Deactivated();
        generation.BroughtForward();
        int c = generation.Deactivated();

        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
        Assert.False(generation.IsCurrent(a));
        Assert.False(generation.IsCurrent(b));
        Assert.True(generation.IsCurrent(c));
    }

    [Theory]
    [InlineData(true, true)]   // pinned
    [InlineData(false, false)] // setting off
    [InlineData(true, false)]
    public void Pinned_or_switched_off_never_hides_automatically(bool pinned, bool setting)
    {
        var drawer = new Drawer(WorkspacePresence.Background) { Pinned = pinned, Setting = setting };
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));

        // ... while an explicit dismissal still does.
        Assert.Equal(WorkspaceAction.Hide, drawer.Request(WorkspaceRequest.Dismiss));
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.None)]
    public void A_launch_after_a_deactivation_is_never_undone_by_it(WorkspacePresence presence, WorkspaceAction launch)
    {
        var drawer = new Drawer(presence);
        int n = drawer.Leave();

        Assert.Equal(launch, drawer.Request(WorkspaceRequest.Launch));

        // A launch that brought Noto forward made the deactivation stale,
        // even if Windows then refused it the foreground (no TRUE arrived).
        if (WorkspaceToggle.BringsForward(launch))
        {
            Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
            Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
        }
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void A_launch_never_hides_whatever_the_generation(WorkspacePresence presence)
    {
        foreach (bool current in new[] { true, false })
        {
            ActivationContext context = new(false, false, false, Ready + 500, Ready, null, HideOnDeactivation: true, GenerationCurrent: current);

            Assert.NotEqual(WorkspaceAction.Hide, WorkspaceToggle.Decide(WorkspaceRequest.Launch, presence, context));
        }
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Minimized, WorkspaceAction.Restore)]
    [InlineData(WorkspacePresence.Background, WorkspaceAction.Focus)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.Hide)]
    public void The_toggle_after_a_deactivation_keeps_its_own_semantics(WorkspacePresence presence, WorkspaceAction toggle)
    {
        var drawer = new Drawer(presence);
        int n = drawer.Leave();

        Assert.Equal(toggle, drawer.Request(WorkspaceRequest.Toggle));

        // Brought forward: the deactivation is stale. Hidden by the toggle:
        // the deactivation finds nothing to hide.
        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
    }

    [Theory]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void A_dismissal_after_a_deactivation_hides_once(WorkspacePresence presence)
    {
        var drawer = new Drawer(presence);
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.Hide, drawer.Request(WorkspaceRequest.Dismiss));
        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
        Assert.Equal(WorkspacePresence.Hidden, drawer.Presence);
    }

    [Fact]
    public void A_hide_does_not_advance_the_generation()
    {
        // Hides need no generation: the pending request finds the window hidden.
        Assert.False(WorkspaceToggle.BringsForward(WorkspaceAction.Hide));
        Assert.False(WorkspaceToggle.BringsForward(WorkspaceAction.None));
        Assert.True(WorkspaceToggle.BringsForward(WorkspaceAction.Show));
        Assert.True(WorkspaceToggle.BringsForward(WorkspaceAction.Restore));
        Assert.True(WorkspaceToggle.BringsForward(WorkspaceAction.Focus));
    }

    [Fact]
    public void A_current_deactivation_while_closing_does_nothing()
    {
        var drawer = new Drawer(WorkspacePresence.Background) { ShuttingDown = true };
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
        Assert.Equal(WorkspacePresence.Background, drawer.Presence);
    }

    [Fact]
    public void A_current_deactivation_during_a_resize_does_nothing()
    {
        var drawer = new Drawer(WorkspacePresence.Background) { Resizing = true };
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
        Assert.Equal(WorkspacePresence.Background, drawer.Presence);
    }

    [Fact]
    public void A_current_deactivation_during_startup_does_nothing()
    {
        var drawer = new Drawer(WorkspacePresence.Background) { RequestTime = Ready - 300 };
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
        Assert.Equal(WorkspacePresence.Background, drawer.Presence);
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    public void A_current_deactivation_of_a_hidden_or_minimized_window_does_nothing(WorkspacePresence presence)
    {
        var drawer = new Drawer(presence);
        int n = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(n));
        Assert.Equal(presence, drawer.Presence);
    }

    // ------------------------------------------------- never hidden and active

    /// <summary>Shown, hidden by a current deactivation, then activated by Windows (the Alt+Tab switcher completing to Noto).</summary>
    private static (Drawer Drawer, int Left, int Returned) HiddenThenActivated()
    {
        var drawer = new Drawer(WorkspacePresence.Foreground);
        int left = drawer.Leave();
        Assert.Equal(WorkspaceAction.Hide, drawer.Deactivated(left));

        return (drawer, left, drawer.Return());
    }

    [Fact]
    public void Hidden_and_activated_is_shown()
    {
        (Drawer drawer, _, int returned) = HiddenThenActivated();

        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Fact]
    public void Repeated_activation_shows_once_and_never_hides()
    {
        (Drawer drawer, _, int returned) = HiddenThenActivated();
        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));

        // The show brought Noto forward, and Windows reported the activation it caused.
        int fromOwnShow = drawer.Return();

        Assert.Equal(WorkspaceAction.None, drawer.Activated(fromOwnShow));
        Assert.Equal(WorkspaceAction.None, drawer.Activated(returned));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Fact]
    public void A_stale_deactivation_after_the_reconciling_show_does_not_hide()
    {
        (Drawer drawer, int left, int returned) = HiddenThenActivated();
        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(left));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Fact]
    public void A_fresh_deactivation_after_the_reconciling_show_hides_while_current()
    {
        (Drawer drawer, _, int returned) = HiddenThenActivated();
        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));

        int leftAgain = drawer.Leave();

        Assert.Equal(WorkspaceAction.Hide, drawer.Deactivated(leftAgain));
        Assert.Equal(WorkspacePresence.Hidden, drawer.Presence);
    }

    [Fact]
    public void A_fresh_deactivation_after_the_reconciling_show_is_stale_once_activation_returned()
    {
        (Drawer drawer, _, int returned) = HiddenThenActivated();
        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));

        int leftAgain = drawer.Leave();
        _ = drawer.Return();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(leftAgain));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Fact]
    public void A_launch_after_the_reconciling_show_never_hides()
    {
        (Drawer drawer, _, int returned) = HiddenThenActivated();
        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));

        Assert.Equal(WorkspaceAction.None, drawer.Request(WorkspaceRequest.Launch));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Theory]
    [InlineData(true, true)]   // pinned
    [InlineData(false, false)] // hide-on-deactivation off
    public void Hidden_and_activated_is_shown_even_when_pinned_or_switched_off(bool pinned, bool setting)
    {
        // Hidden by the hotkey: a deactivation would not hide it here.
        var drawer = new Drawer(WorkspacePresence.Foreground) { Pinned = pinned, Setting = setting };
        Assert.Equal(WorkspaceAction.Hide, drawer.Request(WorkspaceRequest.Toggle));
        int returned = drawer.Return();

        Assert.Equal(WorkspaceAction.Show, drawer.Activated(returned));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Theory]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void Visible_and_activated_is_left_alone(WorkspacePresence presence)
    {
        var drawer = new Drawer(presence);
        int returned = drawer.Return();

        Assert.Equal(WorkspaceAction.None, drawer.Activated(returned));
        Assert.Equal(presence, drawer.Presence);
    }

    [Fact]
    public void Hidden_and_deactivated_is_left_alone()
    {
        var drawer = new Drawer(WorkspacePresence.Hidden);
        int left = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Deactivated(left));
        Assert.Equal(WorkspacePresence.Hidden, drawer.Presence);
    }

    [Fact]
    public void Minimized_and_activated_is_restored()
    {
        var drawer = new Drawer(WorkspacePresence.Minimized);
        int returned = drawer.Return();

        Assert.Equal(WorkspaceAction.Restore, drawer.Activated(returned));
        Assert.Equal(WorkspacePresence.Foreground, drawer.Presence);
    }

    [Fact]
    public void Activation_while_closing_does_not_resurrect_the_window()
    {
        var drawer = new Drawer(WorkspacePresence.Hidden) { ShuttingDown = true };
        int returned = drawer.Return();

        Assert.Equal(WorkspaceAction.None, drawer.Activated(returned));
        Assert.Equal(WorkspacePresence.Hidden, drawer.Presence);
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden, WorkspaceAction.Show)]
    [InlineData(WorkspacePresence.Foreground, WorkspaceAction.None)]
    public void Activation_during_startup_keeps_the_startup_semantics(WorkspacePresence presence, WorkspaceAction expected)
    {
        var drawer = new Drawer(presence) { RequestTime = Ready - 300 };
        int returned = drawer.Return();

        Assert.Equal(expected, drawer.Activated(returned));
    }

    [Fact]
    public void An_activation_that_activation_left_again_before_it_was_decided_is_stale()
    {
        (Drawer drawer, _, int returned) = HiddenThenActivated();
        _ = drawer.Leave();

        Assert.Equal(WorkspaceAction.None, drawer.Activated(returned));
        Assert.Equal(WorkspacePresence.Hidden, drawer.Presence);
    }
}
