using Noto.Core.Links;
using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Links;

/// <summary>File and folder shortcuts (#196): which lines are shortcuts, what a dropped file becomes, and the details line.</summary>
public sealed class FileShortcutsTests
{
    [Fact]
    public void A_line_that_is_one_local_link_is_a_shortcut_to_its_path()
    {
        const string text = "intro\n[report.pdf](file:///C:/Docs/report%20final.pdf)\n[Projects](file:///D:/Projects/)";
        MarkdownDocument document = MarkdownDocument.Parse(text);

        var shortcuts = FileShortcuts.In(document).ToList();

        Assert.Equal([@"C:\Docs\report final.pdf", @"D:\Projects\"], shortcuts.Select(s => s.Path));
        Assert.Equal("report.pdf", text.Substring(shortcuts[0].Name.Start, shortcuts[0].Name.Length));
        Assert.Equal(6, shortcuts[0].Line.Start);
        Assert.Equal("file:///C:/Docs/report%20final.pdf", shortcuts[0].Destination);
    }

    [Theory]
    [InlineData("see [report](file:///C:/report.pdf) here")]
    [InlineData("[report](file:///C:/report.pdf) and more")]
    [InlineData("[site](https://example.com)")]
    [InlineData("[share](file://server/share/a.txt)")]
    [InlineData("- [report](file:///C:/report.pdf)")]
    [InlineData("![picture](attachments/01J00000000000000000000A01.png)")]
    [InlineData("`[report](file:///C:/report.pdf)`")]
    public void A_link_inside_a_sentence_a_web_link_a_network_path_or_a_picture_is_not_a_shortcut(string text)
    {
        Assert.Empty(FileShortcuts.In(MarkdownDocument.Parse(text)));
    }

    [Theory]
    [InlineData(@"C:\a.png", PictureDrop.KeepCopy, TextFileDrop.Shortcut, FileDrop.KeepPicture)]
    [InlineData(@"C:\a.PNG", PictureDrop.Shortcut, TextFileDrop.Shortcut, FileDrop.Link)]
    [InlineData(@"C:\a.txt", PictureDrop.KeepCopy, TextFileDrop.Shortcut, FileDrop.Link)]
    [InlineData(@"C:\a.txt", PictureDrop.KeepCopy, TextFileDrop.InsertText, FileDrop.InsertText)]
    [InlineData(@"C:\a.MD", PictureDrop.KeepCopy, TextFileDrop.InsertText, FileDrop.InsertText)]
    [InlineData(@"C:\a.markdown", PictureDrop.Shortcut, TextFileDrop.InsertText, FileDrop.InsertText)]
    [InlineData(@"C:\a.pdf", PictureDrop.Shortcut, TextFileDrop.InsertText, FileDrop.Link)]
    [InlineData(@"C:\Folder", PictureDrop.KeepCopy, TextFileDrop.InsertText, FileDrop.Link)]
    public void Each_file_becomes_what_its_setting_says(string path, PictureDrop pictures, TextFileDrop texts, FileDrop expected)
    {
        Assert.Equal(expected, FileShortcuts.Choose(path, pictures, texts));
    }

    [Fact]
    public void The_defaults_are_the_behaviour_before_the_settings()
    {
        Assert.Equal(FileDrop.KeepPicture, FileShortcuts.Choose(@"C:\a.jpg", default, default));
        Assert.Equal(FileDrop.Link, FileShortcuts.Choose(@"C:\a.txt", default, default));
    }

    [Fact]
    public void A_picture_with_no_place_of_its_own_is_kept_and_one_with_nowhere_to_keep_it_is_linked()
    {
        // A picture dragged from a web page is a copy removed after the drop: a shortcut to it would lead nowhere.
        Assert.Equal(FileDrop.KeepPicture, FileShortcuts.Choose(@"C:\Temp\a.png", PictureDrop.Shortcut, TextFileDrop.Shortcut, hasOwnPath: false));
        Assert.Equal(FileDrop.Link, FileShortcuts.Choose(@"C:\a.png", PictureDrop.KeepCopy, TextFileDrop.Shortcut, keepsPictures: false));
    }

    [Theory]
    [InlineData(@"C:\a.pdf", true, false, "PDF Document", 2_516_582L, "PDF Document · 2.4 MB")]
    [InlineData(@"C:\a.pdf", true, false, "  ", 900L, "PDF file · 900 bytes")]
    [InlineData(@"C:\a.pdf", true, false, null, null, "PDF file")]
    [InlineData(@"C:\README", true, false, null, 1L, "File · 1 byte")]
    [InlineData(@"C:\Projects", true, true, "File folder", null, "Folder")]
    [InlineData(@"C:\gone.pdf", false, false, null, null, "No longer there")]
    public void The_details_line_says_type_and_size_folder_or_gone(string path, bool exists, bool folder, string? type, long? bytes, string expected)
    {
        Assert.Equal(expected, FileShortcuts.Details(path, exists, folder, type, bytes));
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(2458L, "2.4 KB")]
    [InlineData(14_336L, "14 KB")]
    [InlineData(1_048_576L, "1 MB")]
    [InlineData(5_368_709_120L, "5 GB")]
    public void Sizes_count_in_steps_of_1024(long bytes, string expected)
    {
        Assert.Equal(expected, FileShortcuts.Size(bytes));
    }
}
