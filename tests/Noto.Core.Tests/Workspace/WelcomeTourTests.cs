using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Workspace;

/// <summary>The welcome's steps, their words, and how far it may move (ADR-021).</summary>
public sealed class WelcomeTourTests
{
    [Fact]
    public void The_tour_is_four_steps_in_order_and_starts_at_the_first()
    {
        WelcomeTour tour = WelcomeTour.For("Ctrl+Alt+Win+Space");

        Assert.Equal(["Welcome to Noto", "Organize", "Write", "Your data"], tour.Steps.Select(s => s.Title));
        Assert.Equal(0, tour.Index);
        Assert.Same(tour.Steps[0], tour.Current);
        Assert.True(tour.IsFirst);
        Assert.False(tour.IsLast);
        Assert.All(tour.Steps, s => Assert.NotEmpty(s.Lines));
        Assert.All(tour.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Summary)));
    }

    [Fact]
    public void Next_walks_to_the_last_step_and_no_further()
    {
        WelcomeTour tour = WelcomeTour.For(null);

        Assert.True(tour.Next());
        Assert.Equal(1, tour.Index);
        Assert.False(tour.IsFirst);
        Assert.True(tour.Next());
        Assert.True(tour.Next());
        Assert.Equal(3, tour.Index);
        Assert.True(tour.IsLast);

        Assert.False(tour.Next());
        Assert.Equal(3, tour.Index);
        Assert.Same(tour.Steps[3], tour.Current);
    }

    [Fact]
    public void Back_walks_to_the_first_step_and_no_further()
    {
        WelcomeTour tour = WelcomeTour.For(null);
        Assert.False(tour.Back());
        Assert.Equal(0, tour.Index);

        _ = tour.Next();
        _ = tour.Next();
        Assert.True(tour.Back());
        Assert.Equal(1, tour.Index);
        Assert.True(tour.Back());
        Assert.True(tour.IsFirst);
        Assert.False(tour.Back());
        Assert.Equal(0, tour.Index);
    }

    [Fact]
    public void Where_the_reader_is_is_said_in_words_and_the_primary_action_finishes_only_on_the_last_step()
    {
        WelcomeTour tour = WelcomeTour.For(null);

        string[] progress = new string[4];
        string[] primary = new string[4];
        for (int i = 0; i < 4; i++)
        {
            progress[i] = tour.Progress;
            primary[i] = tour.PrimaryLabel;
            _ = tour.Next();
        }

        Assert.Equal(["Step 1 of 4", "Step 2 of 4", "Step 3 of 4", "Step 4 of 4"], progress);
        Assert.Equal(["Next", "Next", "Next", "Get started"], primary);
    }

    [Fact]
    public void The_first_step_tells_the_shortcut_the_user_has_or_how_to_turn_it_on()
    {
        Assert.Contains("Press Ctrl+Shift+N in any app to show or hide Noto.", WelcomeTour.For("Ctrl+Shift+N").Steps[0].Lines);

        foreach (IReadOnlyList<string> lines in new[] { null, "", "  " }.Select(off => WelcomeTour.For(off).Steps[0].Lines))
        {
            Assert.Contains("Turn on Show and hide Noto in Settings to bring Noto up from any app.", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("Press", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Each_step_covers_what_it_is_for()
    {
        string Text(int step) => string.Join(" ", WelcomeTour.For("X").Steps[step].Lines);

        Assert.Contains("Ctrl+W", Text(0), StringComparison.Ordinal);
        Assert.Contains("notification area", Text(0), StringComparison.Ordinal);
        Assert.Contains("Ctrl+N", Text(1), StringComparison.Ordinal);
        Assert.Contains("pin", Text(1), StringComparison.Ordinal);
        Assert.Contains("fold", Text(1), StringComparison.Ordinal);
        Assert.Contains("move", Text(1), StringComparison.Ordinal);
        Assert.Contains("Drag", Text(1), StringComparison.Ordinal);
        Assert.Contains("saves as you type", Text(2), StringComparison.Ordinal);
        Assert.Contains("Markdown", Text(2), StringComparison.Ordinal);
        Assert.Contains("Ctrl+B", Text(2), StringComparison.Ordinal);
        Assert.Contains("picture", Text(2), StringComparison.Ordinal);
        Assert.Contains("data folder", Text(3), StringComparison.Ordinal);
        Assert.Contains("backs everything up", Text(3), StringComparison.Ordinal);
        Assert.Contains("Export", Text(3), StringComparison.Ordinal);
        Assert.Contains("Import", Text(3), StringComparison.Ordinal);
        Assert.Contains("Settings > Help", Text(3), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_tour_moves_on_its_own()
    {
        WelcomeTour first = WelcomeTour.For(null);
        _ = first.Next();

        Assert.Equal(0, WelcomeTour.For(null).Index);
    }
}
