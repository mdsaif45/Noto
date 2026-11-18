using Noto.Core.Editing;
using Noto.Core.Images;
using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Images;

public sealed class ImageReferenceTests
{
    [Theory]
    [InlineData("png")]
    [InlineData(".JPG")]
    [InlineData("webp")]
    public void A_new_reference_is_a_ulid_named_file_in_the_attachments_folder(string type)
    {
        string reference = ImageReference.New(type);

        Assert.StartsWith("attachments/", reference, StringComparison.Ordinal);
        Assert.True(ImageReference.TryFileName(reference, out string name));
        Assert.Equal(reference["attachments/".Length..], name);
        Assert.EndsWith("." + type.TrimStart('.').ToLowerInvariant(), name, StringComparison.Ordinal);
        Assert.NotEqual(reference, ImageReference.New(type));
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("svg")]
    [InlineData("")]
    public void Only_image_types_are_kept(string type)
    {
        Assert.Throws<ArgumentException>(() => ImageReference.New(type));
        Assert.Null(ImageReference.TypeOf("file." + type));
    }

    [Theory]
    [InlineData("attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.png", true)]
    [InlineData("attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.jpeg", true)]
    [InlineData("attachments/../noto.db", false)]
    [InlineData("attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.png/../../x", false)]
    [InlineData("attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.exe", false)]
    [InlineData("attachments/holiday.png", false)]
    [InlineData("attachments/01JABCDEFGHJKMNPQRSTVWXYZI.png", false)]
    [InlineData("ATTACHMENTS/01JABCDEFGHJKMNPQRSTVWXYZ0.png", false)]
    [InlineData("C:/attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.png", false)]
    [InlineData("https://example.com/a.png", false)]
    [InlineData(null, false)]
    public void Only_a_name_Noto_made_is_a_reference(string? reference, bool made)
    {
        // A dropped or pasted name is untrusted input (data-model.md): never a path, never a traversal.
        Assert.Equal(made, ImageReference.TryFileName(reference, out _));
    }

    [Fact]
    public void The_references_in_a_note_are_its_images_sources_that_Noto_made()
    {
        const string a = "01JABCDEFGHJKMNPQRSTVWXYZ0.png";
        const string b = "01JABCDEFGHJKMNPQRSTVWXYZ1.gif";
        string content = $"Title\n\n![one](attachments/{a})\ntext [link](attachments/{b}) ![web](https://x.com/y.png)\n`![code](attachments/{b})`\n- ![](attachments/{b})";

        Assert.Equal([a, b], ImageReference.In(content));
    }

    // Lines: "Title" 0-5; "![red](attachments/a.png)" 6-31, its caption 8-11; "after ![one](…b.png) ![two](…c.png)" 32-89.
    private const string Pictures = "Title\n![red](attachments/a.png)\nafter ![one](attachments/b.png) ![two](attachments/c.png)";

    [Theory]
    [InlineData(6, "attachments/a.png")] // the picture's line, at its start
    [InlineData(31, "attachments/a.png")] // the end of that line: a right-click on the picture drawn below it
    [InlineData(9, "attachments/a.png")] // in its caption
    [InlineData(32, "attachments/c.png")] // a line of two, outside both: the last, which is drawn on top
    [InlineData(40, "attachments/b.png")] // a line of two, on the first
    [InlineData(3, null)] // a line with no picture
    public void The_picture_at_a_position_is_its_lines(int position, string? source)
    {
        Assert.Equal(source, ImageReference.At(MarkdownDocument.Parse(Pictures), position));
    }

    [Theory]
    [InlineData("![red square](attachments/a.png)", "red square")] // its caption, as shown
    [InlineData("![holiday.JPG](attachments/a.jpg)", "holiday")] // a dropped file's name: the type is the file's
    [InlineData("![notes.txt](attachments/a.png)", "notes.txt")] // not a picture's extension: part of the name
    [InlineData(@"![a \[draft\]: v2?](attachments/a.png)", "a [draft] v2")] // unescaped, without what Windows refuses
    [InlineData("![ ... ](attachments/01J000000000000000000000AB.png)", "01J000000000000000000000AB")] // nothing left: the file's name
    [InlineData("![](attachments/a.webp)", "a")] // no caption
    [InlineData("no picture", null)]
    public void A_picture_is_saved_under_its_caption_or_else_its_files_name(string content, string? name)
    {
        Assert.Equal(name, ImageReference.SaveNameAt(MarkdownDocument.Parse(content), 3));
    }

    [Theory]
    [InlineData(5, "attachments/a.png")] // the picture's line
    [InlineData(37, null)] // the line under it, in the same paragraph: no picture there (measured, Invoke-Images 16)
    [InlineData(54, null)] // the end of the note, on that line
    public void A_picture_is_never_found_from_another_line_of_its_paragraph(int position, string? source)
    {
        const string content = "Red\n![red square](attachments/a.png)\nafter the picture";
        Assert.Equal(source, ImageReference.At(MarkdownDocument.Parse(content), position));
    }

    [Theory]
    [InlineData(6, 31, "attachments/a.png")] // the whole line
    [InlineData(8, 11, "attachments/a.png")] // only its caption, the part shown
    [InlineData(5, 32, "attachments/a.png")] // with the line breaks around it
    [InlineData(8, 10, null)] // part of the caption: text, not the picture
    [InlineData(0, 31, null)] // with the title
    [InlineData(32, 63, null)] // with other text ("after ")
    [InlineData(8, 8, null)] // a caret
    public void Only_a_selection_of_the_picture_alone_is_the_picture(int start, int end, string? source)
    {
        Assert.Equal(source, ImageReference.Selected(MarkdownDocument.Parse(Pictures), start, end));
    }

    [Theory]
    [InlineData("", 0, "![a.png](attachments/x.png)", 27)]
    [InlineData("line", 4, "line\n![a.png](attachments/x.png)", 32)]
    [InlineData("line\nnext", 0, "![a.png](attachments/x.png)\nline\nnext", 27)]
    [InlineData("ab", 1, "a\n![a.png](attachments/x.png)\nb", 29)]
    public void An_image_is_inserted_on_a_line_of_its_own(string text, int at, string result, int caret)
    {
        TextEdit edit = MarkdownCommands.InsertImage(text, TextSelection.Caret(at), "attachments/x.png", "a.png");

        Assert.Equal(result, edit.ApplyTo(text));
        Assert.Equal(TextSelection.Caret(caret), edit.After);
    }

    [Fact]
    public void An_images_name_cannot_close_its_brackets_or_break_its_line()
    {
        TextEdit edit = MarkdownCommands.InsertImage(string.Empty, TextSelection.Caret(0), "attachments/x.png", "a]b\n[c");

        Assert.Equal("![a\\]b \\[c](attachments/x.png)", edit.ApplyTo(string.Empty));
    }
}
