using Noto.Core.Editing;
using Noto.Core.Images;
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
