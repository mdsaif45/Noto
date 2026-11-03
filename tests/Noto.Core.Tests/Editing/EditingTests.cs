using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>The edit, diff and undo model the editor host uses (ADR-017 §3).</summary>
public sealed class EditingTests
{
    // ---------------------------------------------------------------- TextEdit and TextDiff

    [Fact]
    public void An_edit_replaces_its_range_and_nothing_else()
    {
        var edit = new TextEdit(2, 3, "XY", TextSelection.Caret(4));

        Assert.Equal("abXYf", edit.ApplyTo("abcdef"));
    }

    [Theory]
    [InlineData("abc", "abXc", 2, 0, "X")]
    [InlineData("abc", "ac", 1, 1, "")]
    [InlineData("abc", "aXYc", 1, 1, "XY")]
    [InlineData("", "a", 0, 0, "a")]
    [InlineData("aaa", "aaaa", 3, 0, "a")]
    [InlineData("👍b", "👍xb", 2, 0, "x")]
    public void The_diff_is_the_smallest_single_replacement(string before, string after, int start, int length, string replacement)
    {
        TextEdit edit = TextDiff.Between(before, after, TextSelection.Caret(0))!.Value;

        Assert.Equal((start, length, replacement), (edit.Start, edit.Length, edit.Replacement));
        Assert.Equal(after, edit.ApplyTo(before));
    }

    [Fact]
    public void No_change_is_no_edit()
    {
        Assert.Null(TextDiff.Between("same", "same", TextSelection.Caret(0)));
    }

    [Fact]
    public void Any_two_texts_diff_to_an_edit_that_turns_one_into_the_other()
    {
        var random = new Random(7);
        const string alphabet = "ab *\n👍";

        for (int i = 0; i < 2_000; i++)
        {
            string before = new([.. Enumerable.Range(0, random.Next(0, 12)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            string after = new([.. Enumerable.Range(0, random.Next(0, 12)).Select(_ => alphabet[random.Next(alphabet.Length)])]);

            Assert.Equal(after, TextDiff.Between(before, after, TextSelection.Caret(0)) is { } edit ? edit.ApplyTo(before) : before);
        }
    }

    // ---------------------------------------------------------------- EditHistory

    private static (string Text, EditHistory History) Type(string start, params string[] keys)
    {
        var history = new EditHistory();
        string text = start;

        foreach (string key in keys)
        {
            int at = text.Length;
            var edit = new TextEdit(at, 0, key, TextSelection.Caret(at + key.Length));
            history.Record(text, edit, EditKind.Typing, TextSelection.Caret(at));
            text = edit.ApplyTo(text);
        }

        return (text, history);
    }

    [Fact]
    public void Undo_takes_back_a_run_of_typing_word_by_word_and_redo_restores_it()
    {
        (string text, EditHistory history) = Type(string.Empty, "h", "i", " ", "y", "o", "u");
        Assert.Equal("hi you", text);

        text = history.Undo()!.Value.ApplyTo(text);
        Assert.Equal("hi", text);
        text = history.Undo()!.Value.ApplyTo(text);
        Assert.Equal(string.Empty, text);
        Assert.False(history.CanUndo);

        text = history.Redo()!.Value.ApplyTo(text);
        text = history.Redo()!.Value.ApplyTo(text);
        Assert.Equal("hi you", text);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_restores_the_selection_from_before_the_step()
    {
        var history = new EditHistory();
        var edit = new TextEdit(0, 3, "**abc**", TextSelection.Range(2, 5));
        history.Record("abc", edit, EditKind.Other, TextSelection.Range(0, 3));

        TextEdit undo = history.Undo()!.Value;

        Assert.Equal(("abc", TextSelection.Range(0, 3)), (undo.ApplyTo("**abc**"), undo.After));
    }

    [Fact]
    public void A_command_is_always_its_own_step()
    {
        (string text, EditHistory history) = Type(string.Empty, "a", "b");
        var bold = new TextEdit(0, 2, "**ab**", TextSelection.Range(2, 4));
        history.Record(text, bold, EditKind.Other, TextSelection.Range(0, 2));
        text = bold.ApplyTo(text);

        Assert.Equal("ab", history.Undo()!.Value.ApplyTo(text));
    }

    [Fact]
    public void Two_commands_in_a_row_are_two_steps()
    {
        var history = new EditHistory();
        var bold = new TextEdit(0, 1, "**a**", TextSelection.Range(2, 3));
        history.Record("a", bold, EditKind.Other, TextSelection.Range(0, 1));
        var italic = new TextEdit(2, 1, "*a*", TextSelection.Range(3, 4));
        history.Record("**a**", italic, EditKind.Other, TextSelection.Range(2, 3));

        Assert.Equal("**a**", history.Undo()!.Value.ApplyTo("***a***"));
    }

    [Fact]
    public void Backspace_and_delete_runs_are_each_one_step_and_undo_puts_back_exactly_what_went()
    {
        var history = new EditHistory();
        string text = "abcdef";

        // Backspace twice at the end: "f", then "e".
        foreach (int at in new[] { 5, 4 })
        {
            var edit = new TextEdit(at, 1, string.Empty, TextSelection.Caret(at));
            history.Record(text, edit, EditKind.Deletion, TextSelection.Caret(at + 1));
            text = edit.ApplyTo(text);
        }

        // Delete twice at the start: "a", then "b".
        var forward = new List<TextEdit> { new(0, 1, string.Empty, TextSelection.Caret(0)), new(0, 1, string.Empty, TextSelection.Caret(0)) };
        foreach (TextEdit edit in forward)
        {
            history.Record(text, edit, EditKind.Deletion, TextSelection.Caret(0));
            text = edit.ApplyTo(text);
        }

        Assert.Equal("cd", text);
        text = history.Undo()!.Value.ApplyTo(text);
        Assert.Equal("abcd", text);
        text = history.Undo()!.Value.ApplyTo(text);
        Assert.Equal("abcdef", text);
    }

    [Fact]
    public void A_new_edit_after_undo_ends_redo_and_clear_forgets_everything()
    {
        (string text, EditHistory history) = Type(string.Empty, "a");
        text = history.Undo()!.Value.ApplyTo(text);
        history.Record(text, new TextEdit(0, 0, "b", TextSelection.Caret(1)), EditKind.Typing, TextSelection.Caret(0));

        Assert.False(history.CanRedo);
        history.Clear();
        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
    }

    [Fact]
    public void Typing_after_a_caret_jump_is_a_new_step()
    {
        var history = new EditHistory();
        history.Record("ab", new TextEdit(2, 0, "c", TextSelection.Caret(3)), EditKind.Typing, TextSelection.Caret(2));
        history.Record("abc", new TextEdit(0, 0, "x", TextSelection.Caret(1)), EditKind.Typing, TextSelection.Caret(0));

        Assert.Equal("abc", history.Undo()!.Value.ApplyTo("xabc"));
    }
}
