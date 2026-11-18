using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>
/// Clear formatting (D21, #235): the inline markup of every span the selection touches removed in one edit, the text
/// kept. The selection is written with '{' and '}' (a caret is a single '|'), since '[' and ']' are a link's.
/// </summary>
public sealed class ClearFormattingTests
{
    [Theory]
    [InlineData("a {**bold**} b", "a {bold} b")]
    [InlineData("a {*it*} b", "a {it} b")]
    [InlineData("a {_it_} b", "a {it} b")]
    [InlineData("a {__bold__} b", "a {bold} b")]
    [InlineData("a {~~gone~~} b", "a {gone} b")]
    [InlineData("a {==under==} b", "a {under} b")]
    [InlineData("a {::marked::} b", "a {marked} b")]
    [InlineData("a {`code`} b", "a {code} b")]
    [InlineData("a {``co`de``} b", "a {co`de} b")]
    public void Each_mark_goes_and_its_text_stays(string before, string after) => AssertCleared(before, after);

    [Theory]
    [InlineData("{***both***}", "{both}")]
    [InlineData("{**bold *and it* ~~gone~~**}", "{bold and it gone}")]
    [InlineData("{a **b** c *d* e}", "{a b c d e}")]
    public void Nested_and_neighbouring_marks_all_go(string before, string after) => AssertCleared(before, after);

    [Theory]
    [InlineData("see {[docs](https://example.com)} now", "see {docs} now")]
    [InlineData("{[**docs**](https://example.com)}", "{docs}")]
    [InlineData("{**see [docs](notes/a.md)**}", "{see docs}")]
    public void A_link_keeps_its_text_and_loses_its_target(string before, string after) => AssertCleared(before, after);

    [Theory]
    [InlineData("{**go to https://example.com**}", "{go to https://example.com}")]
    [InlineData("{**https://example.com**}", "{https://example.com}")]
    public void A_bare_address_is_text_already_and_stays(string before, string after) => AssertCleared(before, after);

    [Theory]
    [InlineData("{**a** ![*red* square](attachments/a.png)}", "{a ![*red* square](attachments/a.png)}")]
    [InlineData("{[![logo](attachments/a.png)](https://example.com)}", "{![logo](attachments/a.png)}")]
    public void A_picture_is_not_formatting_and_stays_whole(string before, string after) => AssertCleared(before, after);

    [Theory]
    [InlineData("{# **Title**\n- *item*\n- [ ] ~~task~~\n> ==quote==}", "{# Title\n- item\n- [ ] task\n> quote}")]
    [InlineData("{1. **one**\n- [x] `done`}", "{1. one\n- [x] done}")]
    public void Lines_keep_their_heading_list_task_and_quote_markers(string before, string after) => AssertCleared(before, after);

    [Fact]
    public void A_code_block_is_not_formatting_and_stays()
    {
        Assert.Null(MarkdownCommands.ClearFormatting("```\n**not bold**\n```", TextSelection.Range(0, 20)));
    }

    [Theory]
    [InlineData("**bold {and} more** after", "bold {and} more after")]
    [InlineData("**bo{ld** pl}ain", "bo{ld pl}ain")]
    [InlineData("*one* {two} **three**", "*one* {two} **three**")]
    [InlineData("**a**{ b }**c**", "**a**{ b }**c**")]
    public void A_span_the_selection_cuts_through_is_cleared_whole_and_others_stay(string before, string after) =>
        AssertCleared(before, after, applies: before != after);

    [Theory]
    [InlineData("a **bo|ld** b", "a bo|ld b")]
    [InlineData("a **bold|** b", "a bold| b")]
    [InlineData("see [do|cs](https://example.com)", "see do|cs")]
    public void A_caret_clears_the_spans_around_it(string before, string after) => AssertCleared(before, after);

    [Theory]
    [InlineData("plain| text")]
    [InlineData("**a** b|c")]
    [InlineData("{plain text}")]
    [InlineData("")]
    public void Nothing_formatted_is_nothing_to_do(string marked)
    {
        (string text, TextSelection selection) = Parse(marked);

        Assert.Null(MarkdownCommands.ClearFormatting(text, selection));
    }

    [Fact]
    public void It_is_one_edit_so_one_undo_step_brings_every_mark_back()
    {
        (string text, TextSelection selection) = Parse("{**a** *b* [c](https://example.com) `d`}");
        var history = new EditHistory();

        TextEdit edit = MarkdownCommands.ClearFormatting(text, selection)!.Value;
        history.Record(text, edit, EditKind.Other, selection);
        string cleared = edit.ApplyTo(text);

        Assert.Equal("a b c d", cleared);
        TextEdit undo = history.Undo()!.Value;
        Assert.Equal(text, undo.ApplyTo(cleared));
        Assert.Null(history.Undo());
    }

    private static void AssertCleared(string before, string after, bool applies = true)
    {
        (string text, TextSelection selection) = Parse(before);
        TextEdit? edit = MarkdownCommands.ClearFormatting(text, selection);

        if (!applies)
        {
            Assert.Null(edit);
            return;
        }

        TextEdit cleared = edit ?? throw new Xunit.Sdk.XunitException("the command did not apply");
        Assert.Equal(after, Write(cleared.ApplyTo(text), cleared.After));
    }

    /// <summary>"a {b} c" is "a b c" with "b" selected; "a b|c" has a caret.</summary>
    private static (string Text, TextSelection Selection) Parse(string marked)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);

        if (caret >= 0)
        {
            return (marked.Remove(caret, 1), TextSelection.Caret(caret));
        }

        int open = marked.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
        {
            return (marked, TextSelection.Caret(0));
        }

        int close = marked.IndexOf('}', StringComparison.Ordinal) - 1;
        return (marked.Remove(close + 1, 1).Remove(open, 1), TextSelection.Range(open, close));
    }

    private static string Write(string text, TextSelection selection) =>
        selection.IsCaret ? text.Insert(selection.Start, "|") : text.Insert(selection.End, "}").Insert(selection.Start, "{");
}
