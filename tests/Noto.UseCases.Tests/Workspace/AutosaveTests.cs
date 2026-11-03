using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.UseCases.Tests.Workspace;

/// <summary>When typed text is written (parity B19, P2.1): after a pause, and never later than the bound.</summary>
public sealed class AutosaveTests
{
    [Fact]
    public void The_bounds_are_a_second_of_quiet_and_five_seconds_at_most()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Autosave.Quiet);
        Assert.Equal(TimeSpan.FromSeconds(5), Autosave.Longest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000)]
    [InlineData(3_999)]
    [InlineData(4_000)]
    public void Text_unsaved_for_a_while_waits_for_a_full_pause(int unsavedMs)
    {
        Assert.Equal(Autosave.Quiet, Autosave.Delay(TimeSpan.FromMilliseconds(unsavedMs)));
    }

    [Theory]
    [InlineData(4_001, 999)]
    [InlineData(4_500, 500)]
    [InlineData(4_999, 1)]
    public void Near_the_bound_the_pause_is_cut_short_so_the_write_lands_on_it(int unsavedMs, int delayMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(delayMs), Autosave.Delay(TimeSpan.FromMilliseconds(unsavedMs)));
    }

    [Theory]
    [InlineData(5_000)]
    [InlineData(5_001)]
    [InlineData(60_000)]
    public void At_or_past_the_bound_it_is_written_at_once(int unsavedMs)
    {
        Assert.Equal(TimeSpan.Zero, Autosave.Delay(TimeSpan.FromMilliseconds(unsavedMs)));
    }

    [Fact]
    public void Steady_typing_is_written_by_the_bound_however_fast_the_keys_come()
    {
        // A key every 200 ms, so the quiet pause never happens: the write must still land at 5 s, not later.
        TimeSpan now = TimeSpan.Zero;
        TimeSpan due = now + Autosave.Delay(now);

        while (now + TimeSpan.FromMilliseconds(200) < due)
        {
            now += TimeSpan.FromMilliseconds(200);
            due = now + Autosave.Delay(now);
        }

        Assert.Equal(Autosave.Longest, due);
    }
}
