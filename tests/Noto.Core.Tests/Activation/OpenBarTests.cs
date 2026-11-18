using Noto.Core.Activation;
using Xunit;

namespace Noto.Core.Tests.Activation;

/// <summary>
/// The Open Bar's hide modes (#185 part 2; parity A3): what the bar looks like and what its one timer does, without a
/// window. What the running application does with a real pointer is Invoke-Settings.ps1 case 24's to prove.
/// </summary>
public sealed class OpenBarTests
{
    [Fact]
    public void A_new_bar_is_hidden_until_set()
    {
        Assert.Equal(OpenBarLook.Hidden, new OpenBar().Look);
    }

    [Fact]
    public void Off_is_never_there()
    {
        var bar = new OpenBar();

        Assert.Equal(EdgeStep.None, bar.Set(OpenBarMode.Off, away: false));
        Assert.Equal(OpenBarLook.Hidden, bar.Look);
        Assert.Equal(EdgeStep.None, bar.PointerMoved());
        Assert.Equal(EdgeStep.None, bar.Elapsed());
        Assert.Equal(OpenBarLook.Hidden, bar.Look);
    }

    [Fact]
    public void Always_shown_is_shown_while_noto_is_hidden_with_no_timer_and_never_fades()
    {
        var bar = new OpenBar();

        Assert.Equal(EdgeStep.None, bar.Set(OpenBarMode.AlwaysShown, away: false));
        Assert.Equal(OpenBarLook.Shown, bar.Look);
        Assert.Equal(EdgeStep.None, bar.PointerMoved());
        Assert.Equal(EdgeStep.None, bar.Elapsed());
        Assert.Equal(OpenBarLook.Shown, bar.Look);
    }

    [Theory]
    [InlineData(OpenBarMode.AlwaysShown)]
    [InlineData(OpenBarMode.HideWhenIdle)]
    public void Away_noto_shown_full_screen_or_locked_hides_it_and_stops_the_timer(OpenBarMode mode)
    {
        var bar = new OpenBar();
        bar.Set(mode, away: false);

        Assert.Equal(mode == OpenBarMode.HideWhenIdle ? EdgeStep.Disarm : EdgeStep.None, bar.Set(mode, away: true));
        Assert.Equal(OpenBarLook.Hidden, bar.Look);
        Assert.Equal(EdgeStep.None, bar.PointerMoved());
        Assert.Equal(OpenBarLook.Hidden, bar.Look);
    }

    [Fact]
    public void Hide_when_idle_appears_drawn_counts_down_and_fades_once()
    {
        var bar = new OpenBar();

        Assert.Equal(EdgeStep.Arm, bar.Set(OpenBarMode.HideWhenIdle, away: false));
        Assert.Equal(OpenBarLook.Shown, bar.Look);

        // A repeated set (another foreground change) neither restarts the count nor fades it early.
        Assert.Equal(EdgeStep.None, bar.Set(OpenBarMode.HideWhenIdle, away: false));

        // One shot: the fire fades it and stops the timer; a stray second fire changes nothing.
        Assert.Equal(EdgeStep.Disarm, bar.Elapsed());
        Assert.Equal(OpenBarLook.Faded, bar.Look);
        Assert.Equal(EdgeStep.None, bar.Elapsed());
        Assert.Equal(OpenBarLook.Faded, bar.Look);

        // Faded stays faded through another set.
        Assert.Equal(EdgeStep.None, bar.Set(OpenBarMode.HideWhenIdle, away: false));
        Assert.Equal(OpenBarLook.Faded, bar.Look);
    }

    [Fact]
    public void The_pointer_over_a_faded_bar_draws_it_again_and_restarts_the_count()
    {
        var bar = new OpenBar();
        bar.Set(OpenBarMode.HideWhenIdle, away: false);
        bar.Elapsed();

        Assert.Equal(EdgeStep.Arm, bar.PointerMoved());
        Assert.Equal(OpenBarLook.Shown, bar.Look);
        Assert.Equal(EdgeStep.Arm, bar.PointerMoved());

        Assert.Equal(EdgeStep.Disarm, bar.Elapsed());
        Assert.Equal(OpenBarLook.Faded, bar.Look);
    }

    [Fact]
    public void Coming_back_after_noto_was_shown_starts_drawn_with_a_fresh_count()
    {
        var bar = new OpenBar();
        bar.Set(OpenBarMode.HideWhenIdle, away: false);
        bar.Elapsed();

        bar.Set(OpenBarMode.HideWhenIdle, away: true);
        Assert.Equal(EdgeStep.Arm, bar.Set(OpenBarMode.HideWhenIdle, away: false));
        Assert.Equal(OpenBarLook.Shown, bar.Look);
    }

    [Fact]
    public void A_fire_that_lands_after_noto_was_shown_does_not_fade_the_next_appearance()
    {
        var bar = new OpenBar();
        bar.Set(OpenBarMode.HideWhenIdle, away: false);
        bar.Set(OpenBarMode.HideWhenIdle, away: true);

        Assert.Equal(EdgeStep.None, bar.Elapsed());
        bar.Set(OpenBarMode.HideWhenIdle, away: false);
        Assert.Equal(OpenBarLook.Shown, bar.Look);
    }

    [Fact]
    public void Switching_modes_while_there_starts_or_stops_the_count()
    {
        var bar = new OpenBar();

        bar.Set(OpenBarMode.AlwaysShown, away: false);
        Assert.Equal(EdgeStep.Arm, bar.Set(OpenBarMode.HideWhenIdle, away: false));

        bar.Elapsed();
        Assert.Equal(OpenBarLook.Faded, bar.Look);

        // Always shown again: drawn at once, nothing counting.
        Assert.Equal(EdgeStep.None, bar.Set(OpenBarMode.AlwaysShown, away: false));
        Assert.Equal(OpenBarLook.Shown, bar.Look);

        bar.Set(OpenBarMode.HideWhenIdle, away: false);
        Assert.Equal(EdgeStep.Disarm, bar.Set(OpenBarMode.Off, away: false));
        Assert.Equal(OpenBarLook.Hidden, bar.Look);
    }
}
