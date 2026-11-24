using Noto.Core.Images;
using Noto.Core.Markdown;
using Noto.Core.Notes;
using Xunit;

namespace Noto.Core.Tests.Images;

public sealed class NotePicturesTests
{
    // Lines: "Title" 0-5; "![red](attachments/a.png)" 6-31, its caption 8-11, "]" at 11, its source 13-30;
    // "after ![one](attachments/b.png) ![](https://x.com/c.png)" from 32, the second picture 38-63.
    private const string Pictures = "Title\n![red](attachments/a.png)\nafter ![one](attachments/b.png) ![](https://x.com/c.png)";

    [Fact]
    public void A_notes_pictures_are_listed_in_the_order_written_the_first_shown()
    {
        NotePictures pictures = NotePictures.Of(MarkdownDocument.Parse(Pictures))!;

        Assert.Equal(["attachments/a.png", "attachments/b.png", "https://x.com/c.png"], pictures.All.Select(p => p.Source));
        Assert.Equal(["red", "one", "Picture"], pictures.All.Select(p => p.Name));
        Assert.Equal(0, pictures.Index);
        Assert.Equal("Picture 1 of 3", pictures.Position);
    }

    [Fact]
    public void A_web_picture_is_listed_but_is_no_file_Noto_keeps_so_it_is_never_fetched()
    {
        NotePicture web = NotePictures.Of(MarkdownDocument.Parse(Pictures))!.All[2];

        Assert.False(ImageReference.TryFileName(web.Source, out _));
    }

    [Theory]
    [InlineData("no pictures here")]
    [InlineData("")]
    public void A_note_without_pictures_has_nothing_to_preview(string content)
    {
        Assert.Null(NotePictures.Of(MarkdownDocument.Parse(content)));
    }

    [Fact]
    public void A_code_note_has_no_pictures_its_text_is_as_typed()
    {
        Assert.Null(NotePictures.Of(MarkdownDocument.Parse(Pictures, NoteMode.Code)));
        Assert.Null(NotePictures.At(MarkdownDocument.Parse(Pictures, NoteMode.Code), 6, 31));
    }

    [Fact]
    public void Left_and_right_stop_at_the_ends()
    {
        NotePictures first = NotePictures.Of(MarkdownDocument.Parse(Pictures))!;

        Assert.Equal(0, first.Step(-1).Index);
        Assert.Equal(1, first.Step(1).Index);
        Assert.Equal(2, first.Step(1).Step(1).Step(1).Index);
        Assert.Equal("Picture 3 of 3", first.Step(5).Position);
        Assert.Equal(1, first.Step(5).Step(-1).Index);
    }

    [Theory]
    [InlineData(6, 31, 0)] // the picture's whole line
    [InlineData(8, 11, 0)] // its caption alone, the part shown
    [InlineData(38, 63, 1)] // the second picture, on a line with text
    [InlineData(7, 7, 0)] // a caret inside its hidden markup: "!|["
    [InlineData(12, 12, 0)] // "]|("
    [InlineData(20, 20, 0)] // in its source
    [InlineData(30, 30, 0)] // before its closing ")"
    public void Space_previews_the_picture_selected_alone_or_with_the_caret_in_its_markup(int start, int end, int index)
    {
        Assert.Equal(index, NotePictures.At(MarkdownDocument.Parse(Pictures), start, end)?.Index);
    }

    [Theory]
    [InlineData(6, 6)] // before the picture
    [InlineData(8, 8)] // at the start of its caption: typing there is typing its caption
    [InlineData(9, 9)] // in its caption
    [InlineData(11, 11)] // at the end of its caption
    [InlineData(31, 31)] // after the picture, at the end of its line
    [InlineData(3, 3)] // in plain text
    [InlineData(8, 10)] // part of its caption
    [InlineData(0, 31)] // the picture and the title
    public void Space_anywhere_else_types_a_space(int start, int end)
    {
        Assert.Null(NotePictures.At(MarkdownDocument.Parse(Pictures), start, end));
    }
}
