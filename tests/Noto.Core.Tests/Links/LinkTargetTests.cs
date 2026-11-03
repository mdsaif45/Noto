using Noto.Core.Editing;
using Noto.Core.Links;
using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Links;

public sealed class LinkTargetTests
{
    [Theory]
    [InlineData("https://example.com/a?b=c#d", LinkKind.Web, "https://example.com/a?b=c#d")]
    [InlineData("HTTP://Example.com", LinkKind.Web, "http://example.com/")]
    [InlineData("www.example.com/x", LinkKind.Web, "https://www.example.com/x")]
    [InlineData("mailto:me@example.com", LinkKind.Mail, "mailto:me@example.com")]
    [InlineData("file:///C:/Users/me/report.pdf", LinkKind.Local, @"C:\Users\me\report.pdf")]
    [InlineData("file:///D:/Projects%20Old/", LinkKind.Local, @"D:\Projects Old\")]
    public void Allowed_destinations_resolve_to_what_is_opened(string destination, LinkKind kind, string address)
    {
        Assert.Equal((kind, address), LinkTarget.Resolve(destination));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("vbscript:msgbox")]
    [InlineData("ms-settings:privacy")]
    [InlineData("shell:startup")]
    [InlineData("file://server/share/x.txt")]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData("file:///relative/path")]
    [InlineData(@"C:\Users\me\report.pdf")]
    [InlineData("report.pdf")]
    [InlineData("https://exa mple.com")]
    [InlineData("https://example.com/\u0001")]
    [InlineData("https://")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_refused(string? destination)
    {
        // ADR-019 §1: other schemes, network paths, bare paths, white space. A bare drive path is refused too: a note
        // links a file through file:, which is what Noto writes.
        Assert.Equal((LinkKind.Refused, (string?)null), LinkTarget.Resolve(destination));
        Assert.Equal(LinkKind.Refused, LinkTarget.Classify(destination));
    }

    [Theory]
    [InlineData(@"C:\a\report.pdf", false)]
    [InlineData(@"C:\a\notes.TXT", false)]
    [InlineData(@"C:\a\photo.jpeg", false)]
    [InlineData(@"C:\a\setup.exe", true)]
    [InlineData(@"C:\a\run.bat", true)]
    [InlineData(@"C:\a\script.ps1", true)]
    [InlineData(@"C:\a\link.lnk", true)]
    [InlineData(@"C:\a\installer.msi", true)]
    [InlineData(@"C:\a\page.hta", true)]
    [InlineData(@"C:\a\no-extension", true)]
    [InlineData(@"C:\a\unknown.xyz", true)]
    public void Programs_scripts_shortcuts_and_the_unknown_ask_first(string path, bool asks)
    {
        Assert.Equal(asks, LinkTarget.NeedsConfirmation(path));
    }

    [Fact]
    public void A_local_path_is_written_as_a_file_uri_that_resolves_back()
    {
        string written = LinkTarget.ForPath(@"C:\My Files\plan (draft).docx");

        Assert.Equal("file:///C:/My%20Files/plan%20%28draft%29.docx", written);
        Assert.Equal((LinkKind.Local, @"C:\My Files\plan (draft).docx"), LinkTarget.Resolve(written));
        Assert.Throws<ArgumentException>(() => LinkTarget.ForPath("relative.txt"));
    }

    [Theory]
    [InlineData("See [the RFC](https://example.com/rfc) now", 6, "https://example.com/rfc")]
    [InlineData("See [the RFC](https://example.com/rfc) now", 12, "https://example.com/rfc")]
    [InlineData("See [the RFC](https://example.com/rfc) now", 3, null)]
    [InlineData("Go to https://example.com/x. Then", 10, "https://example.com/x")]
    [InlineData("Go to https://example.com/x. Then", 27, "https://example.com/x")]
    [InlineData("Go to https://example.com/x. Then", 28, null)]
    [InlineData(@"[a](https://example.com/a\)b)", 1, "https://example.com/a)b")]
    [InlineData("line one\n[two](www.example.com)", 10, "www.example.com")]
    public void The_destination_at_a_position_is_the_target_or_the_bare_address(string source, int position, string? destination)
    {
        Assert.Equal(destination, LinkTarget.At(MarkdownDocument.Parse(source), position));
    }

    [Fact]
    public void A_web_address_pasted_over_a_selection_links_it()
    {
        TextEdit edit = Assert.NotNull(MarkdownCommands.LinkSelection("read the spec today", new TextSelection(9, 13), " https://example.com/spec\n"));

        Assert.Equal("read the [spec](https://example.com/spec) today", edit.ApplyTo("read the spec today"));
        Assert.Equal(TextSelection.Caret(9 + "[spec](https://example.com/spec)".Length), edit.After);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x.txt")]
    [InlineData("https://a.com https://b.com")]
    public void Anything_else_pasted_over_a_selection_is_an_ordinary_paste(string pasted)
    {
        Assert.Null(MarkdownCommands.LinkSelection("read the spec today", new TextSelection(9, 13), pasted));
    }

    [Fact]
    public void A_caret_or_a_selection_over_lines_is_never_linked()
    {
        Assert.Null(MarkdownCommands.LinkSelection("abc", TextSelection.Caret(1), "https://example.com"));
        Assert.Null(MarkdownCommands.LinkSelection("a\nb", new TextSelection(0, 3), "https://example.com"));
    }

    [Fact]
    public void Local_paths_become_one_link_each_named_for_the_file_or_folder()
    {
        TextEdit edit = MarkdownCommands.LinkPaths("x", TextSelection.Caret(1), [@"C:\Docs\a [1].pdf", @"D:\Projects\", @"E:\"]);

        Assert.Equal(
            "x[a \\[1\\].pdf](file:///C:/Docs/a%20[1].pdf)\n[Projects](file:///D:/Projects/)\n[E:\\](file:///E:/)",
            edit.ApplyTo("x"));
    }
}
