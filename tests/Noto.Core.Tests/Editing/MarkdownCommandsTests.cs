using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>
/// The formatting commands as pure transforms. Each case writes the selection into the text with '[' and ']' (a
/// caret is a single '|'), and expects the text and selection after the command in the same notation.
/// </summary>
public sealed class MarkdownCommandsTests
{
    [Theory]
    [InlineData("a [bold] b", "a **[bold]** b")]
    [InlineData("a bo|ld b", "a **bo|ld** b")]
    [InlineData("a **[bold]** b", "a [bold] b")]
    [InlineData("a **bo|ld** b", "a bo|ld b")]
    [InlineData("a [**bold**] b", "a [bold] b")]
    [InlineData("a | b", "a **|** b")]
    [InlineData("[ padded ]", " **[padded]** ")]
    [InlineData("[one\ntwo]", "[**one**\n**two**]")]
    [InlineData("***[x]***", "*[x]*")]
    public void Strong_wraps_or_unwraps(string before, string after)
    {
        AssertCommand(MarkdownCommands.ToggleStrong, before, after);
    }

    [Theory]
    [InlineData("[word]", "*[word]*")]
    [InlineData("*[word]*", "[word]")]
    [InlineData("**[word]**", "***[word]***")]
    public void Emphasis_wraps_inside_strong_and_unwraps(string before, string after)
    {
        AssertCommand(MarkdownCommands.ToggleEmphasis, before, after);
    }

    [Theory]
    [InlineData("[gone]", "~~[gone]~~")]
    [InlineData("~~[gone]~~", "[gone]")]
    public void Strikethrough_wraps_or_unwraps(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleStrikethrough, before, after);

    [Theory]
    [InlineData("[under]", "==[under]==")]
    [InlineData("==[under]==", "[under]")]
    public void Underline_wraps_or_unwraps(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleUnderline, before, after);

    [Theory]
    [InlineData("run [ls -la] now", "run `[ls -la]` now")]
    [InlineData("run `[ls]` now", "run [ls] now")]
    public void Code_wraps_or_unwraps(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleCode, before, after);

    [Theory]
    [InlineData("Ti|tle", "# Ti|tle")]
    [InlineData("# Ti|tle", "## Ti|tle")]
    [InlineData("## Ti|tle", "### Ti|tle")]
    [InlineData("### Ti|tle", "Ti|tle")]
    [InlineData("- Ti|tle", "# Ti|tle")]
    [InlineData("[a\nb]", "[# a\n# b]")]
    public void Heading_cycles_one_two_three_and_back(string before, string after) =>
        AssertCommand(MarkdownCommands.CycleHeading, before, after);

    [Theory]
    [InlineData("it|em", "- it|em")]
    [InlineData("- it|em", "it|em")]
    [InlineData("[a\n\nb]", "[- a\n\n- b]")]
    [InlineData("[- a\n- b]", "[a\nb]")]
    [InlineData("[- a\nb]", "[- a\n- b]")]
    [InlineData("1. it|em", "- it|em")]
    [InlineData("  it|em", "  - it|em")]
    [InlineData("- [ ] it|em", "- it|em")]
    public void Bullets_toggle_per_line_keeping_indent_and_text(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleBullets, before, after);

    [Theory]
    [InlineData("[a\nb\nc]", "[1. a\n2. b\n3. c]")]
    [InlineData("[1. a\n2. b]", "[a\nb]")]
    [InlineData("[- a\n- b]", "[1. a\n2. b]")]
    public void Numbers_count_from_one(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleNumbers, before, after);

    [Theory]
    [InlineData("bu|y milk", "- [ ] bu|y milk")]
    [InlineData("- bu|y milk", "- [ ] bu|y milk")]
    [InlineData("- [x] bu|y milk", "bu|y milk")]
    public void Tasks_toggle(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleTasks, before, after);

    [Theory]
    [InlineData("- [ ] bu|y", "- [x] bu|y")]
    [InlineData("- [x] bu|y", "- [ ] bu|y")]
    [InlineData("[- [x] a\n- [ ] b]", "[- [x] a\n- [x] b]")]
    [InlineData("[- [x] a\n- [X] b]", "[- [ ] a\n- [ ] b]")]
    public void Checking_a_task_checks_the_open_ones_or_unchecks_all(string before, string after) =>
        AssertCommand(MarkdownCommands.ToggleTaskDone, before, after);

    [Fact]
    public void Checking_without_a_task_does_nothing()
    {
        (string text, TextSelection selection) = Parse("plain |line");

        Assert.Null(MarkdownCommands.ToggleTaskDone(text, selection));
    }

    [Theory]
    [InlineData("see [docs] here", "see [docs](|) here")]
    [InlineData("see do|cs here", "see [docs](|) here")]
    [InlineData("see | here", "see [|]() here")]
    public void A_link_takes_the_selection_or_word_as_its_text(string before, string after) =>
        AssertCommand(MarkdownCommands.InsertLink, before, after);

    [Fact]
    public void A_code_block_fences_the_lines()
    {
        AssertCommand(MarkdownCommands.WrapInCodeBlock, "[a\nb]", "```\na\nb|\n```");
    }

    [Theory]
    [InlineData("- milk|", "- milk\n- |")]
    [InlineData("- [x] milk|", "- [x] milk\n- [ ] |")]
    [InlineData("9. nine|", "9. nine\n10. |")]
    [InlineData("3) three|", "3) three\n4) |")]
    [InlineData("  - nested|", "  - nested\n  - |")]
    [InlineData("- spl|it", "- spl\n- |it")]
    [InlineData("- |", "|")]
    [InlineData("a\n- [ ] |", "a\n|")]
    public void Enter_in_a_list_continues_it_or_ends_it_on_an_empty_item(string before, string after) =>
        AssertCommand(MarkdownCommands.ContinueList, before, after);

    [Theory]
    [InlineData("plain|")]
    [InlineData("# heading|")]
    [InlineData("[- a]")]
    [InlineData("|- a")]
    public void Enter_elsewhere_is_an_ordinary_break(string before)
    {
        (string text, TextSelection selection) = Parse(before);

        Assert.Null(MarkdownCommands.ContinueList(text, selection));
    }

    [Theory]
    [InlineData("- a\n- b|", "- a\n  - b|")]
    [InlineData("1. a\n2. b|", "1. a\n   2. b|")]
    [InlineData("- [ ] a\n- [ ] b|", "- [ ] a\n  - [ ] b|")]
    [InlineData("[- a\n- b]", "[  - a\n  - b]")]
    public void Tab_in_a_list_nests_each_item_by_its_markers_width(string before, string after) =>
        AssertCommand(MarkdownCommands.IndentListItems, before, after);

    [Theory]
    [InlineData("- a\n  - b|", "- a\n- b|")]
    [InlineData("- a\n    - b|", "- a\n  - b|")]
    [InlineData("- a\n\t- b|", "- a\n- b|")]
    public void Shift_tab_in_a_list_takes_one_level_off(string before, string after) =>
        AssertCommand(MarkdownCommands.OutdentListItems, before, after);

    [Theory]
    [InlineData("plain|")]
    [InlineData("[- a\nplain]")]
    [InlineData("# heading|")]
    public void Tab_outside_a_list_is_not_an_indent_so_it_moves_focus(string before)
    {
        (string text, TextSelection selection) = Parse(before);

        Assert.Null(MarkdownCommands.IndentListItems(text, selection));
    }

    [Fact]
    public void Shift_tab_at_the_left_edge_does_nothing()
    {
        (string text, TextSelection selection) = Parse("- a|");

        Assert.Null(MarkdownCommands.OutdentListItems(text, selection));
    }

    [Theory]
    [InlineData("one\ntw|o\nthree", "tw|o\none\nthree")]
    [InlineData("one\n[two\nthree]", "[two\nthree]\none")]
    [InlineData("a\n[b\n]c", "[b\n]a\nc")]
    public void Alt_up_moves_the_lines_above_the_one_before(string before, string after) =>
        AssertCommand(MarkdownCommands.MoveLinesUp, before, after);

    [Theory]
    [InlineData("on|e\ntwo\nthree", "two\non|e\nthree")]
    [InlineData("[one\ntwo]\nthree", "three\n[one\ntwo]")]
    public void Alt_down_moves_the_lines_below_the_one_after(string before, string after) =>
        AssertCommand(MarkdownCommands.MoveLinesDown, before, after);

    [Theory]
    [InlineData("fi|rst\nsecond", true)]
    [InlineData("first\nsec|ond", false)]
    [InlineData("on|ly", true)]
    [InlineData("on|ly", false)]
    public void Moving_past_the_edge_does_nothing(string before, bool up)
    {
        (string text, TextSelection selection) = Parse(before);

        Assert.Null(up ? MarkdownCommands.MoveLinesUp(text, selection) : MarkdownCommands.MoveLinesDown(text, selection));
    }

    [Fact]
    public void Every_command_on_every_caret_of_a_realistic_note_gives_a_valid_edit_or_nothing()
    {
        const string note = "# Title\n- [ ] **task** one\n1. first\n> quote `code`\n```\nfence\n```\nplain *em* [l](u) ~~s~~";
        Func<string, TextSelection, TextEdit?>[] commands =
        [
            MarkdownCommands.ToggleStrong, MarkdownCommands.ToggleEmphasis, MarkdownCommands.ToggleStrikethrough,
            MarkdownCommands.ToggleUnderline, MarkdownCommands.ToggleCode, MarkdownCommands.CycleHeading,
            MarkdownCommands.ToggleBullets, MarkdownCommands.ToggleNumbers, MarkdownCommands.ToggleTasks,
            MarkdownCommands.ToggleTaskDone, MarkdownCommands.WrapInCodeBlock, MarkdownCommands.InsertLink, MarkdownCommands.ContinueList,
            MarkdownCommands.IndentListItems, MarkdownCommands.OutdentListItems, MarkdownCommands.MoveLinesUp, MarkdownCommands.MoveLinesDown,
        ];

        foreach (Func<string, TextSelection, TextEdit?> command in commands)
        {
            for (int at = 0; at <= note.Length; at++)
            {
                foreach (TextSelection selection in new[] { TextSelection.Caret(at), TextSelection.Range(at, Math.Min(note.Length, at + 5)) })
                {
                    if (command(note, selection) is not { } edit)
                    {
                        continue;
                    }

                    Assert.InRange(edit.Start, 0, note.Length);
                    Assert.InRange(edit.Start + edit.Length, edit.Start, note.Length);
                    string after = edit.ApplyTo(note);
                    Assert.InRange(edit.After.Anchor, 0, after.Length);
                    Assert.InRange(edit.After.Active, 0, after.Length);
                }
            }
        }
    }

    private static void AssertCommand(Func<string, TextSelection, TextEdit?> command, string before, string after)
    {
        (string text, TextSelection selection) = Parse(before);

        TextEdit edit = command(text, selection) ?? throw new Xunit.Sdk.XunitException("the command did not apply");
        string result = edit.ApplyTo(text);

        Assert.Equal(after, Write(result, edit.After));
    }

    /// <summary>"a [b] c" is "a b c" with "b" selected; "a b|c" has a caret.</summary>
    private static (string Text, TextSelection Selection) Parse(string marked)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);

        if (caret >= 0)
        {
            return (marked.Remove(caret, 1), TextSelection.Caret(caret));
        }

        int open = marked.IndexOf('[', StringComparison.Ordinal);
        int close = marked.LastIndexOf(']') - 1;
        return (marked.Remove(close + 1, 1).Remove(open, 1), TextSelection.Range(open, close));
    }

    private static string Write(string text, TextSelection selection) =>
        selection.IsCaret ? text.Insert(selection.Start, "|") : text.Insert(selection.End, "]").Insert(selection.Start, "[");
}
