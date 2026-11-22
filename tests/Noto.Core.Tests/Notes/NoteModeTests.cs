using Noto.Core.Editing;
using Noto.Core.Markdown;
using Noto.Core.Notes;
using Noto.Core.Portability;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Core.Tests.Notes;

/// <summary>
/// A note's mode (#195; parity D27, D28, D45): Markdown, Code or plain text; a reading of the same text, never a
/// change to it; and the "New notes are" setting.
/// </summary>
public sealed class NoteModeTests
{
    [Fact]
    public void The_modes_are_markdown_first_then_code_and_plain_text()
    {
        // Markdown is 0: a note with no stored mode, and default(NoteMode), are Markdown, as every note before #195 was.
        Assert.Equal([NoteMode.Markdown, NoteMode.Code, NoteMode.PlainText], Enum.GetValues<NoteMode>());
        Assert.Equal(NoteMode.Markdown, default(NoteMode));
        Assert.Equal(NoteMode.Markdown, new Note { Id = NoteId.New(), Content = string.Empty, CreatedAt = default, UpdatedAt = default }.Mode);
    }

    [Theory]
    [InlineData("Markdown", NoteMode.Markdown)]
    [InlineData("Code", NoteMode.Code)]
    [InlineData("PlainText", NoteMode.PlainText)]
    [InlineData(null, NoteMode.Markdown)]
    [InlineData("", NoteMode.Markdown)]
    [InlineData("code", NoteMode.Markdown)]
    [InlineData("1", NoteMode.Markdown)]
    [InlineData("Rich", NoteMode.Markdown)]
    public void A_stored_mode_is_read_by_its_exact_name_and_anything_else_is_markdown(string? stored, NoteMode expected)
    {
        Assert.Equal(expected, NoteModes.Parse(stored));
    }

    [Fact]
    public void Every_mode_reads_back_from_the_name_it_is_stored_as()
    {
        foreach (NoteMode mode in Enum.GetValues<NoteMode>())
        {
            Assert.Equal(mode, NoteModes.Parse(mode.ToString()));
        }
    }

    [Theory]
    [InlineData(NoteMode.Markdown, "Write something… # for a heading, - for a list")]
    [InlineData(NoteMode.Code, "Write something…")]
    [InlineData(NoteMode.PlainText, "Write something…")]
    public void An_empty_notes_placeholder_suggests_markdown_only_in_markdown(NoteMode mode, string expected)
    {
        // #256 (B26): Code and plain text show their # as text, so they are not told it makes a heading.
        Assert.Equal(expected, NoteModes.Placeholder(mode));
    }

    [Fact]
    public void Tab_in_code_types_four_spaces_over_the_selection()
    {
        Assert.Equal("    ", NoteModes.CodeIndent);

        TextEdit caret = NoteModes.IndentCode("ab", TextSelection.Caret(1))!.Value;
        Assert.Equal("a    b", caret.ApplyTo("ab"));
        Assert.Equal(TextSelection.Caret(5), caret.After);

        TextEdit over = NoteModes.IndentCode("abcd", TextSelection.Range(3, 1))!.Value;
        Assert.Equal("a    d", over.ApplyTo("abcd"));
        Assert.Equal(TextSelection.Caret(5), over.After);
    }

    [Fact]
    public void A_code_or_plain_text_note_is_described_with_no_markdown_at_all()
    {
        const string text = "# Heading\r\n**bold** _it_ [link](https://x.org)\n- [ ] task\n```\ncode\n\n> quote ![p](a.png)";

        foreach (NoteMode mode in new[] { NoteMode.Code, NoteMode.PlainText })
        {
            MarkdownDocument raw = MarkdownDocument.Parse(text, mode);

            // The text exactly as typed: no span to style, hide, link or draw, and one block per line, covering it.
            Assert.Same(text, raw.Source);
            Assert.Empty(raw.Spans);
            Assert.Null(raw.LinkAt(text.IndexOf("link", StringComparison.Ordinal)));
            Assert.All(raw.Blocks, b => Assert.Contains(b.Kind, new[] { MarkdownBlockKind.Paragraph, MarkdownBlockKind.Blank }));
            Assert.Equal(
                ["# Heading", "**bold** _it_ [link](https://x.org)", "- [ ] task", "```", "code", string.Empty, "> quote ![p](a.png)"],
                raw.Blocks.Select(b => text.Substring(b.Start, b.Length)));
            Assert.Equal(MarkdownBlockKind.Blank, raw.Blocks[5].Kind);
        }

        // Markdown reads the same text as it always has.
        Assert.NotEmpty(MarkdownDocument.Parse(text, NoteMode.Markdown).Spans);
        Assert.Equal(MarkdownDocument.Parse(text).Spans, MarkdownDocument.Parse(text, NoteMode.Markdown).Spans);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("a\r\nb\rc\n")]
    public void Raw_blocks_cover_every_line_as_parse_does(string text)
    {
        MarkdownDocument raw = MarkdownDocument.Parse(text, NoteMode.Code);

        Assert.Equal(MarkdownDocument.Parse(text).Blocks.Select(b => (b.Start, b.Length)), raw.Blocks.Select(b => (b.Start, b.Length)));
    }

    [Fact]
    public void An_edit_in_a_code_note_restyles_only_its_line()
    {
        MarkdownDocument before = MarkdownDocument.Parse("one\ntwo\nthree", NoteMode.Code);
        MarkdownDocument after = MarkdownDocument.Parse("one\nt#wo\nthree", NoteMode.Code);

        Assert.Equal((4, 4), MarkdownDocument.ChangedRegion(before, after));
    }

    [Theory]
    [InlineData("# Heading\nbody", NoteMode.Markdown, "Heading")]
    [InlineData("# Heading\nbody", NoteMode.Code, "# Heading")]
    [InlineData("# Heading\nbody", NoteMode.PlainText, "# Heading")]
    [InlineData("\n\n   **x** = 1;  \n", NoteMode.Code, "**x** = 1;")]
    [InlineData("  \r\n", NoteMode.PlainText, "")]
    [InlineData(null, NoteMode.Code, "")]
    public void The_title_is_the_first_line_raw_outside_markdown(string? content, NoteMode mode, string title)
    {
        Assert.Equal(title, NoteTitle.From(content, mode));
        Assert.Equal(title, new Note { Id = NoteId.New(), Content = content ?? string.Empty, Mode = mode, CreatedAt = default, UpdatedAt = default }.Title);
    }

    [Fact]
    public void New_notes_are_is_a_notes_setting_that_defaults_to_markdown()
    {
        Assert.Equal("notes.new-mode", SettingKeys.NotesNewMode.Name);
        Assert.Equal(NoteMode.Markdown, SettingKeys.NotesNewMode.Default);
        Assert.Equal(SettingValueKind.Enumeration, SettingKeys.NotesNewMode.Kind);
        Assert.Contains(SettingKeys.NotesNewMode, SettingKeys.All);
        Assert.True(SettingKeys.NotesNewMode.IsValid(NoteMode.Code));
        Assert.True(SettingKeys.NotesNewMode.IsValid(NoteMode.PlainText));
        Assert.False(SettingKeys.NotesNewMode.IsValid((NoteMode)3));
    }

    [Fact]
    public void An_export_index_carries_the_mode_and_one_written_before_it_still_reads()
    {
        var index = new ExportIndex
        {
            ExportedAt = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero),
            Folders = [new ExportFolder("W", "W", false, null, [new ExportNote("a.md", false, false, null, "Code")])],
        };

        Assert.Equal("Code", ExportIndex.Read(index.ToJson())!.Folders[0].Notes[0].Mode);

        // Version 1 as #150 wrote it: no mode. Read whole, the note Markdown.
        ExportIndex? old = ExportIndex.Read("""{"format":"noto-export","version":1,"exportedAt":"2026-01-01T00:00:00Z","folders":[{"directory":"W","name":"W","pinned":false,"notes":[{"file":"a.md","pinned":true,"folded":false}]}]}""");
        Assert.NotNull(old);
        Assert.True(old.Folders[0].Notes[0].Pinned);
        Assert.Equal(NoteMode.Markdown, NoteModes.Parse(old.Folders[0].Notes[0].Mode));
        Assert.Equal(1, ExportIndex.CurrentVersion);
    }
}
