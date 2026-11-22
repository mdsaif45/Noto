using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Printing's wiring (#248; B21, G25, I6), checked in source as images' and links' are. Where the text goes on a page is
/// Core's (<c>PrintPageTests</c>); that Ctrl+P opens Windows' dialog with Noto still shown, Invoke-Editor.ps1's case 26.
/// What a printout looks like needs a person at the dialog (docs/research/printing.md).
/// </summary>
public sealed class PrintGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Print = Code(Path.Join(Windows, "MainWindow.Print.cs"));
    private static readonly string Printer = Code(Path.Join(Windows, "NotePrinter.cs"));
    private static readonly string ReadView = Code(Path.Join(Windows, "MarkdownReadView.cs"));
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Coordinator = Code(Path.Join(Windows, "WindowCoordinator.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void Ctrl_P_prints_the_current_card_from_the_stack_and_its_editor()
    {
        // On the stack's root, beside Ctrl+N and Ctrl+Shift+M, so a card and the editor inside it both reach it.
        XElement root = MainXaml.Root!.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "NoteRoot");
        XElement print = Assert.Single(root.Elements().Single(e => e.Name.LocalName == "Grid.KeyboardAccelerators").Elements(), e => (string?)e.Attribute("Invoked") == "OnPrintAccelerator");
        Assert.Equal("P", (string?)print.Attribute("Key"));
        Assert.Equal("Control", (string?)print.Attribute("Modifiers"));

        // Ctrl+P was free: the editor's chords keep only Ctrl+Shift+P (insert picture), so plain Ctrl+P passes on.
        Assert.Contains("(VirtualKey.P, true) => EditorCommand.Picture,", Host, StringComparison.Ordinal);
        Assert.DoesNotContain("(VirtualKey.P, false)", Host, StringComparison.Ordinal);
        Assert.Single(MainXaml.Root!.Descendants(), e => e.Name.LocalName == "KeyboardAccelerator" && (string?)e.Attribute("Key") == "P");

        Assert.Matches(@"args\.Handled = true;\s*if \(CurrentCard\(\) is \{ \} id\)\s*\{\s*QueuePrint\(id\);", Body(Print, "OnPrintAccelerator"));
        Assert.Contains("DispatcherQueue.TryEnqueueGuarded(() => PrintCard(id))", Print, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cards_menu_has_Print_with_its_key()
    {
        string menu = Body(Main, "OnNoteListContextRequested");
        Assert.Contains("menu.Items.Add(MenuItem(\"Print…\", \"Ctrl+P\", () => QueuePrint(card.Id), _printer is not { Busy: true }));", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void The_note_prints_as_it_is_on_screen_and_storing_nothing()
    {
        string print = Body(Print, "PrintCard");
        Assert.Matches(@"if \(_stack\.Editing\?\.Value == id\)\s*\{\s*_editor\?\.ReadBack\(\);\s*source = _stack\.Draft;", print);
        Assert.Contains("_printer.Print(card.DisplayTitle, source, card.Mode)", print, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", print, StringComparison.Ordinal);
    }

    [Fact]
    public void The_drawer_is_held_open_from_before_the_dialog_until_its_task_completes()
    {
        string print = Body(Print, "PrintCard");
        int hold = print.IndexOf("HoldOpenChanged?.Invoke(this, true);", StringComparison.Ordinal);
        Assert.True(hold >= 0 && hold < print.IndexOf("_printer.Print(", StringComparison.Ordinal));

        // A dialog that never opened lets go at once and says why; one that did, when its task completes.
        Assert.Matches(@"if \(!await _printer\.Print\(card\.DisplayTitle, source, card\.Mode\)\)\s*\{\s*HoldOpenChanged\?\.Invoke\(this, false\);\s*ShowNoteStatus\(""Printing isn't available on this PC\.""\);", print);
        Assert.Contains("_printer.Completed += OnPrinted;", print, StringComparison.Ordinal);
        Assert.StartsWith("{\n        HoldOpenChanged?.Invoke(this, false);", Body(Print, "OnPrinted").ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Matches(@"task\.Completed \+= \(_, e\) => _window\.DispatcherQueue\.TryEnqueueGuarded\(\(\) =>\s*\{[\s\S]*?Finish\(\);\s*Completed\?\.Invoke\(this, e\.Completion\);", Printer);

        // The hold is the coordinator's, beside the pin, and never the user's pin itself.
        Assert.Matches(@"main\.HoldOpenChanged \+= \(_, held\) =>\s*\{\s*if \(_coordinator is not null\)\s*\{\s*_coordinator\.HeldOpen = held;", App);
        Assert.Contains("HideOnDeactivation: HideOnDeactivation && !_pinned && !_heldOpen,", Coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPinned", Print, StringComparison.Ordinal);

        // Focus goes back to the note afterwards.
        Assert.Contains("_editor.TakeFocus();", Body(Print, "OnPrinted"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pictures_are_decoded_before_the_dialog_opens()
    {
        // microsoft-ui-xaml#11095: a BitmapImage still decoding prints blank.
        string print = Body(Printer, "Print");
        int decode = print.IndexOf("await Pictures(source, mode)", StringComparison.Ordinal);
        Assert.True(decode >= 0 && decode < print.IndexOf("ShowPrintUIForWindowAsync", StringComparison.Ordinal));

        string pixels = Body(Printer, "Decode");
        Assert.Contains("await decoder.GetSoftwareBitmapAsync(", pixels, StringComparison.Ordinal);
        Assert.Contains("await source.SetBitmapAsync(bitmap);", pixels, StringComparison.Ordinal);
        Assert.DoesNotContain("BitmapImage", Printer, StringComparison.Ordinal);

        // On paper the read view draws those pixels, every picture, never its card's BitmapImage path.
        Assert.Matches(@"if \(_paper is not null\)\s*\{\s*picture = Printed\(_paper, ReferenceOf\(document, block, image\), markup\);", ReadView);
        string printed = Body(ReadView, "Printed");
        Assert.Contains("paper.Pictures.TryGetValue(reference, out PrintedPicture? picture)", printed, StringComparison.Ordinal);
        Assert.Contains("ImageSize.Fit(picture.Width, picture.Height, paper.Width, paper.PictureHeight)", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("Bitmap(", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_is_Windows_own_registered_once_and_refused_plainly_where_it_is_missing()
    {
        string print = Body(Printer, "Print");
        Assert.Contains("if (!PrintManager.IsSupported())", print, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(Printer, @"PrintTaskRequested \+="));
        Assert.Matches(@"if \(_document is null\)\s*\{[^}]*PrintManagerInterop\.GetForWindow\(hwnd\)\.PrintTaskRequested \+= OnPrintTaskRequested;", print);
        Assert.Contains("await PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd);", print, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (ex is ArgumentException", print, StringComparison.Ordinal);

        // Never through a web view or a file on disk.
        Assert.DoesNotContain("WebView", Printer, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTempPath", Printer, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Write", Printer, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_are_the_read_view_in_ink_on_white_flowed_over_pages()
    {
        string paginate = Body(Printer, "OnPaginate");
        Assert.Contains("PrintPage.TextArea(page.PageSize.Width, page.PageSize.Height, page.ImageableRect.X, page.ImageableRect.Y, page.ImageableRect.Width, page.ImageableRect.Height)", paginate, StringComparison.Ordinal);
        Assert.Contains("new MarkdownReadView(job.Source, job.Mode, new MarkdownReadView.Paper(job.Pictures, area.Width, PrintPage.PictureMaxHeight(area)))", paginate, StringComparison.Ordinal);
        Assert.Contains("Background = new SolidColorBrush(Microsoft.UI.Colors.White),", paginate, StringComparison.Ordinal);
        Assert.Contains("new RichTextBlockOverflow()", paginate, StringComparison.Ordinal);
        Assert.Contains("_pages.Count == PrintPage.MaxPages", paginate, StringComparison.Ordinal);

        // Paper is light whatever the theme or high contrast, and a long note prints whole.
        string paper = Regex.Match(ReadView, @"internal MarkdownReadView\(string source, NoteMode mode, Paper paper\)[\s\S]*?\n    \}").Value;
        Assert.Contains("RequestedTheme = ElementTheme.Light;", paper, StringComparison.Ordinal);
        Assert.Contains("Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);", paper, StringComparison.Ordinal);
        Assert.Contains("bool highContrast = _paper is null && ", ReadView, StringComparison.Ordinal);
        Assert.Contains("(source.Length > RenderedLimit && _paper is null)", ReadView, StringComparison.Ordinal);

        // The hidden layout is never hit by the pointer.
        Assert.Contains("new() { Opacity = 0, IsHitTestVisible = false }", Printer, StringComparison.Ordinal);
    }

    [Fact]
    public void Print_traces_say_what_happened_never_the_note()
    {
        string[] traces = [.. Regex.Matches(Printer, @"Trace\.WriteLine\((.*)\);").Select(m => m.Groups[1].Value)];
        Assert.NotEmpty(traces);
        Assert.All(traces, t =>
        {
            Assert.Matches(@"^\$?""Noto\.Print ", t);
            Assert.DoesNotContain("Title", t, StringComparison.Ordinal);
            Assert.DoesNotContain("Source", t, StringComparison.Ordinal);
            Assert.DoesNotContain("source", t, StringComparison.Ordinal);
        });
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)(?: static)?(?: async)? [\w<>?,\[\]().]+? {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced: {method}");
    }

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
