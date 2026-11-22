using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>
/// D23 (#244): with Move checked tasks to the bottom on, checking a task moves it, with its nested lines, to the end of
/// its own list, and unchecking moves it back after the open ones; one edit, so one undo step with the check.
/// </summary>
public sealed class CheckedTasksToBottomTests
{
    [Theory]
    // A flat list: the checked task goes last, after the checked ones already there.
    [InlineData("- [ ] |a\n- [ ] b\n- [ ] c", "- [ ] b\n- [ ] c\n- [x] a")]
    [InlineData("- [ ] |a\n- [ ] b\n- [x] c", "- [ ] b\n- [x] c\n- [x] a")]
    [InlineData("- [ ] a\n- [ ] b\n- [ ] |c", "- [ ] a\n- [ ] b\n- [x] c")]
    // Its nested lines go with it.
    [InlineData("- [ ] |a\n  - [ ] a1\n  more\n- [ ] b", "- [ ] b\n- [x] a\n  - [ ] a1\n  more")]
    // A nested list moves at its own level, inside its parent.
    [InlineData("- [ ] p\n  - [ ] |a\n  - [ ] b\n- [ ] q", "- [ ] p\n  - [ ] b\n  - [x] a\n- [ ] q")]
    // A blank line, a heading, a separator and a less-indented line each end the list.
    [InlineData("- [ ] |a\n- [ ] b\n\n- [ ] c", "- [ ] b\n- [x] a\n\n- [ ] c")]
    [InlineData("- [ ] |a\n- [ ] b\n# H\n- [ ] c", "- [ ] b\n- [x] a\n# H\n- [ ] c")]
    [InlineData("- [ ] |a\n- [ ] b\n***\n- [ ] c", "- [ ] b\n- [x] a\n***\n- [ ] c")]
    [InlineData("text\n- [ ] |a\n- [ ] b\nend", "text\n- [ ] b\n- [x] a\nend")]
    // Another list is another list: a numbered one after a bulleted one, or another bullet.
    [InlineData("- [ ] |a\n- [ ] b\n1. [ ] c", "- [ ] b\n- [x] a\n1. [ ] c")]
    [InlineData("- [ ] |a\n- [ ] b\n* [ ] c", "- [ ] b\n- [x] a\n* [ ] c")]
    // Plain items are open items.
    [InlineData("- [ ] |a\n- b\n- [x] c", "- b\n- [x] c\n- [x] a")]
    // A numbered list keeps counting as it did: the lines move, the numbers stay.
    [InlineData("1. [ ] |a\n2. [ ] b\n3. [ ] c", "1. [ ] b\n2. [ ] c\n3. [x] a")]
    [InlineData("8. [ ] |a\n9. [ ] b\n10. [ ] c", "8. [ ] b\n9. [ ] c\n10. [x] a")]
    [InlineData("1) [ ] |a\n1) [ ] b", "1) [ ] b\n1) [x] a")]
    // Unchecking: back just after the last open item, the checked ones keeping their order.
    [InlineData("- [ ] a\n- [x] b\n- [x] |c", "- [ ] a\n- [ ] c\n- [x] b")]
    [InlineData("- [x] |a\n- [x] b", "- [ ] a\n- [x] b")]
    [InlineData("- [x] b\n- [x] |a", "- [ ] a\n- [x] b")]
    [InlineData("1. [ ] a\n2. [x] b\n3. [x] |c", "1. [ ] a\n2. [ ] c\n3. [x] b")]
    public void A_toggled_task_moves_within_its_own_list(string before, string after)
    {
        (string text, int caret) = Caret(before);

        TextEdit edit = MarkdownCommands.ToggleTaskDone(text, TextSelection.Caret(caret), checkedToBottom: true)!.Value;

        Assert.Equal(after, edit.ApplyTo(text));
    }

    [Theory]
    // Alone in its list, last already, or at the bottom of the open ones: only the box changes.
    [InlineData("- [ ] |a", "- [x] a")]
    [InlineData("- [ ] |a\n\n- [ ] b", "- [x] a\n\n- [ ] b")]
    [InlineData("- [ ] a\n- [x] |b\n- [x] c", "- [ ] a\n- [ ] b\n- [x] c")]
    public void A_task_already_in_its_place_is_only_toggled(string before, string after)
    {
        (string text, int caret) = Caret(before);

        TextEdit edit = MarkdownCommands.ToggleTaskDone(text, TextSelection.Caret(caret), checkedToBottom: true)!.Value;

        Assert.Equal(after, edit.ApplyTo(text));
        Assert.Equal(TextSelection.Caret(caret), edit.After);
    }

    [Fact]
    public void Off_the_task_stays_where_it_is()
    {
        TextEdit edit = MarkdownCommands.ToggleTaskDone("- [ ] a\n- [ ] b", TextSelection.Caret(6), checkedToBottom: false)!.Value;

        Assert.Equal("- [x] a\n- [ ] b", edit.ApplyTo("- [ ] a\n- [ ] b"));
    }

    [Fact]
    public void The_caret_follows_the_moved_task_and_a_caret_elsewhere_follows_its_line()
    {
        const string text = "- [ ] alpha\n- [ ] beta\n- [ ] gamma";

        // In "alpha", after "al": the task ends up on the last line, the caret still after "al".
        TextEdit edit = MarkdownCommands.ToggleTaskDone(text, TextSelection.Caret(8), checkedToBottom: true)!.Value;
        string result = edit.ApplyTo(text);
        Assert.Equal("- [ ] beta\n- [ ] gamma\n- [x] alpha", result);
        Assert.Equal("ph", result.Substring(edit.After.Active, 2));

        // A click on alpha's box with the caret in "gamma": gamma's line moves up one, the caret with it.
        int inGamma = text.IndexOf("mma", StringComparison.Ordinal);
        TextEdit click = MarkdownCommands.ToggleTaskAt(text, 2, TextSelection.Caret(inGamma), checkedToBottom: true)!.Value;
        string clicked = click.ApplyTo(text);
        Assert.Equal("- [ ] beta\n- [ ] gamma\n- [x] alpha", clicked);
        Assert.Equal("mma", clicked.Substring(click.After.Active, 3));
    }

    [Fact]
    public void A_click_off_keeps_the_caret_exactly_where_it_was()
    {
        TextEdit click = MarkdownCommands.ToggleTaskAt("- [ ] a\n- [ ] b", 10, TextSelection.Caret(3), checkedToBottom: false)!.Value;

        Assert.Equal("- [ ] a\n- [x] b", click.ApplyTo("- [ ] a\n- [ ] b"));
        Assert.Equal(TextSelection.Caret(3), click.After);
    }

    [Fact]
    public void A_renumbered_line_keeps_the_caret_on_its_text()
    {
        const string text = "9. [ ] a\n10. [ ] bee";

        // The caret in "bee" (now numbered 9.) stays before its "ee".
        int inBee = text.IndexOf("ee", StringComparison.Ordinal);
        TextEdit edit = MarkdownCommands.ToggleTaskAt(text, 3, TextSelection.Caret(inBee), checkedToBottom: true)!.Value;
        string result = edit.ApplyTo(text);

        Assert.Equal("9. [ ] bee\n10. [x] a", result);
        Assert.Equal("ee", result.Substring(edit.After.Active, 2));
    }

    [Fact]
    public void Several_checked_at_once_go_to_the_bottom_in_their_order()
    {
        const string text = "- [ ] a\n- [ ] b\n- [ ] c\n- [ ] d";

        // "a" through "b" selected.
        TextEdit edit = MarkdownCommands.ToggleTaskDone(text, TextSelection.Range(6, 14), checkedToBottom: true)!.Value;

        Assert.Equal("- [ ] c\n- [ ] d\n- [x] a\n- [x] b", edit.ApplyTo(text));
    }

    [Fact]
    public void The_check_and_the_move_are_one_edit()
    {
        const string text = "Shop\n- [ ] milk\n- [ ] eggs\n\nafter";

        TextEdit edit = MarkdownCommands.ToggleTaskDone(text, TextSelection.Caret(9), checkedToBottom: true)!.Value;

        // One replacement, inside the two lines that changed places: what they have in common at either end is kept.
        Assert.Equal("Shop\n- [ ] ".Length, edit.Start);
        Assert.Equal("eggs\n- [x] milk", edit.Replacement);
        Assert.Equal("Shop\n- [ ] eggs\n- [x] milk\n\nafter", edit.ApplyTo(text));
    }

    /// <summary>"a|b" is "ab" with the caret between.</summary>
    private static (string Text, int Caret) Caret(string marked)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        return (marked.Remove(caret, 1), caret);
    }
}
