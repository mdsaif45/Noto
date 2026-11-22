using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// A note saved or copied as a picture (#254; I4, J31 to J34), checked in source. Its geometry is Core's
/// (<c>NotePictureTests</c>); its settings, the settings guard's; at run time, Copy as picture is Invoke-Images.ps1
/// case 18's. The save dialog is not driven by the harness, so this is its check.
/// </summary>
public sealed class NotePictureGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Picture = Code(Path.Join(Windows, "MainWindow.Picture.cs"));
    private static readonly string ReadView = Code(Path.Join(Windows, "MarkdownReadView.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void The_card_menu_saves_or_copies_the_note_as_a_picture_after_its_markdown_export()
    {
        string menu = Body(Main, "OnNoteListContextRequested");
        const string save = "menu.Items.Add(MenuItem(\"Save as picture…\", string.Empty, () => SaveCardPicture(card.Id)));";
        const string copy = "menu.Items.Add(MenuItem(\"Copy as picture\", string.Empty, () => CopyCardPicture(card.Id)));";

        Assert.True(Index(menu, "\"Export as Markdown…\"") < Index(menu, save));
        Assert.True(Index(menu, save) < Index(menu, copy));
        Assert.True(Index(menu, copy) < Index(menu, "\"Move to the Recycle Bin\""));
    }

    [Fact]
    public void Save_renders_first_then_asks_with_a_save_dialog_owned_by_the_window_and_writes_whole_or_not_at_all()
    {
        string save = Body(Picture, "SaveCardPicture");

        // Rendered before the dialog, so a picture that cannot be made never leaves the dialog's empty file behind.
        Assert.True(Index(save, "await RenderPicture(request)") < Index(save, "new FileSavePicker"));
        Assert.Contains("picker.FileTypeChoices.Add(\"PNG image\", [\".png\"]);", save, StringComparison.Ordinal);
        Assert.Contains("SuggestedFileName = PortableNames.Safe(request.Title),", save, StringComparison.Ordinal);

        // The window owns the dialog, as an unpackaged app's must, so the drawer stays while it is up (as Settings' pickers).
        Assert.Contains("InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));", save, StringComparison.Ordinal);
        Assert.Matches(@"if \(await picker\.PickSaveFileAsync\(\) is not \{ \} file\)\s*\{\s*return;", save);
        Assert.Matches(@"if \(!WritePicture\(file\.Path, png\)\)\s*\{\s*ShowNoteNotice\(""The picture couldn't be saved there\.""\);\s*return;", save);

        // Beside the chosen file, then moved over it; on failure, nothing partial is left, nor the dialog's empty file.
        string write = Body(Picture, "WritePicture");
        Assert.Contains("string partial = path + \".partial\";", write, StringComparison.Ordinal);
        Assert.True(Index(write, "File.WriteAllBytes(partial, png);") < Index(write, "File.Move(partial, path, overwrite: true);"));
        Assert.Matches(@"catch \(Exception ex\) when \(ex is IOException or UnauthorizedAccessException\)\s*\{[\s\S]*?DataPortability\.RemoveEmpty\(path\);\s*return false;", write);
        Assert.Matches(@"finally\s*\{\s*try\s*\{\s*File\.Delete\(partial\);", write);
        Assert.Contains("internal static void RemoveEmpty(string file)", Code(Path.Join(Windows, "DataPortability.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Copy_puts_the_png_on_the_clipboard_as_a_bitmap_and_says_so()
    {
        string copy = Body(Picture, "CopyCardPicture");

        Assert.Contains("package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));", copy, StringComparison.Ordinal);
        Assert.Matches(@"try\s*\{\s*Clipboard\.SetContent\(package\);\s*Clipboard\.Flush\(\);\s*\}\s*catch \(COMException\)\s*\{\s*ShowNoteNotice\(""Nothing was copied: another app is using the clipboard\. Try again\.""\);\s*return;", copy);
        Assert.Contains("ShowNoteStatus(\"Picture copied.\");", copy, StringComparison.Ordinal);
    }

    [Fact]
    public void The_whole_note_is_rendered_at_the_screen_scale_once_its_pictures_are_decoded()
    {
        string render = Body(Picture, "RenderPicture");

        // Built under the drawer (the user's theme), on an unbounded canvas out of view, and taken away whatever happens.
        Assert.Contains("(DataTemplate)WorkspaceContent.Resources[\"NotePictureTemplate\"]", render, StringComparison.Ordinal);
        Assert.Contains("var stage = new Canvas { IsHitTestVisible = false };", render, StringComparison.Ordinal);
        Assert.Contains("WorkspaceContent.Children.Add(stage);", render, StringComparison.Ordinal);
        Assert.Matches(@"finally\s*\{\s*_ = WorkspaceContent\.Children\.Remove\(stage\);", render);

        // The card's font and the note's mode; then pictures decoded, the canvas sized by Core, rendered, encoded as PNG.
        Assert.Contains("view.FontSize = _noteSize;", render, StringComparison.Ordinal);
        Assert.Contains("view.Mode = request.Mode;", render, StringComparison.Ordinal);
        int drawn = Index(render, "await view.Drawn.WaitAsync(");
        int laid = Index(render, "NotePicture.Layout(note.DesiredSize.Width,");
        int scaled = Index(render, "NotePicture.ScaleWithin(layout.Width, layout.Height, screen)");
        int rendered = Index(render, "await bitmap.RenderAsync(root");
        Assert.True(drawn < laid && laid < scaled && scaled < rendered);
        Assert.Contains("double screen = Content.XamlRoot.RasterizationScale;", render, StringComparison.Ordinal);
        Assert.Contains("BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png)", render, StringComparison.Ordinal);
        Assert.Contains("ShowNoteNotice(\"The picture couldn't be made.\");", render, StringComparison.Ordinal);

        // The read view says when its pictures are drawn: each render's, decoded or failed.
        Assert.Contains("internal Task Drawn => Task.WhenAll(_pictures);", ReadView, StringComparison.Ordinal);
        Assert.Contains("_pictures.Add(Picture(holder, ReferenceOf(document, block, image), room, markup));", ReadView, StringComparison.Ordinal);
        Assert.Contains("await decoded.Task;", Body(ReadView, "Picture"), StringComparison.Ordinal);

        // The text on screen, whole: the draft while editing, the full note even when folded.
        Assert.Contains("editing ? _stack.Draft : card.Content,", Body(Picture, "CardPictureOf"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_is_the_cards_fill_the_six_tints_in_palette_order_then_the_read_view_and_its_footer()
    {
        XElement template = MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "NotePictureTemplate");
        XElement root = Assert.Single(template.Elements());
        var children = root.Elements().ToList();

        Assert.Equal("Raw", (string?)root.Attribute("AutomationProperties.AccessibilityView"));
        Assert.Equal(["Border", "Grid", "StackPanel"], children.Select(c => c.Name.LocalName));
        Assert.Equal("{ThemeResource SurfaceCardBrush}", (string?)children[0].Attribute("Background"));
        Assert.Equal(
            Enumerable.Range(1, 6).Select(i => $"{{ThemeResource Note{i}TintBrush}}"),
            children[1].Elements().Select(e => (string?)e.Attribute("Background")));
        Assert.All(children[1].Elements(), e => Assert.Equal("Collapsed", (string?)e.Attribute("Visibility")));
        Assert.Equal(["MarkdownReadView", "Grid"], children[2].Elements().Select(e => e.Name.LocalName));
        Assert.Contains("NoteColor.Keys.ToList().IndexOf(request.ColorKey)", Picture, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trace_never_carries_the_notes_text_or_title()
    {
        var traces = Regex.Matches(Picture, @"Trace\.WriteLine\((.*)\);").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(traces);
        Assert.All(traces, t => Assert.DoesNotMatch(@"request|Content|Title|file\.|path", t));
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)( static)?( async)? [\w?<>\[\],\s]+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
