using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Images' wiring (ADR-019 §4), checked in source as links' is. What a reference is and how large an image is drawn,
/// is Core's (<c>ImageReferenceTests</c>, <c>ImageSizeTests</c>); keeping and sweeping files, Infrastructure's
/// (<c>AttachmentStoreTests</c>); at run time, Invoke-Images.ps1's.
/// </summary>
public sealed class ImageGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Images = Code(Path.Join(Windows, "NoteEditorHost.Images.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string MainImages = Code(Path.Join(Windows, "MainWindow.Images.cs"));
    private static readonly string ReadView = Code(Path.Join(Windows, "MarkdownReadView.cs"));
    private static readonly XDocument HostXaml = XDocument.Load(Path.Join(Windows, "NoteEditorHost.xaml"));

    [Fact]
    public void A_picture_is_pasted_only_when_the_clipboard_has_no_files_or_text()
    {
        string paste = Body(Host, "PasteAsync");
        Assert.Matches(@"if \(paths\.Count == 0 && plain is null && html is null && clipboard\.Contains\(StandardDataFormats\.Bitmap\)\)\s*\{\s*picture = await clipboard\.GetBitmapAsync\(\);", paste);
        Assert.Matches(@"if \(picture is not null\)\s*\{\s*await AddPicture\(picture\);\s*return;", paste);

        // Kept as PNG, whatever format it came in; a refusal is said, never silent.
        string picture = Body(Images, "AddPicture");
        Assert.Contains("Images.Keep(png, \"png\")", picture, StringComparison.Ordinal);
        Assert.Contains("Apply(MarkdownCommands.InsertImage(_text, _selection, reference, \"Pasted image\"), record: true);", picture, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(picture, @"ImageNotice\?\.Invoke\(this, Explain\("));
    }

    [Fact]
    public void Image_files_are_kept_as_copies_and_other_files_linked()
    {
        string files = Body(Images, "AddFiles");
        Assert.Contains("var images = paths.Where(p => Images is not null && ImageReference.TypeOf(p) is not null).ToList();", files, StringComparison.Ordinal);
        Assert.Contains("var others = paths.Except(images).ToList();", files, StringComparison.Ordinal);
        Assert.Contains("Images!.KeepCopyOf(path)", files, StringComparison.Ordinal);
        Assert.Matches(@"if \(outcome == ImageKeeping\.Kept && reference is not null\)\s*\{\s*Apply\(MarkdownCommands\.InsertImage\(_text, _selection, reference, Path\.GetFileName\(path\)\), record: true\);\s*\}\s*else\s*\{\s*ImageNotice\?\.Invoke\(this, Explain\(outcome\)\);", files);
    }

    [Fact]
    public void Dropped_files_are_copied_in_where_they_were_dropped()
    {
        XElement box = HostXaml.Root!.Descendants().Single(e => e.Name.LocalName == "EditorBox");
        Assert.Equal("True", (string?)box.Attribute("AllowDrop"));
        Assert.Contains("NoteEditor.AddHandler(DragEnterEvent, new DragEventHandler(OnDragOver), handledEventsToo: true);", Host, StringComparison.Ordinal);
        Assert.Contains("NoteEditor.AddHandler(DragOverEvent, new DragEventHandler(OnDragOver), handledEventsToo: true);", Host, StringComparison.Ordinal);
        Assert.Contains("NoteEditor.AddHandler(DropEvent, new DragEventHandler(OnDrop), handledEventsToo: true);", Host, StringComparison.Ordinal);

        // Files, and text from another app (#31), are accepted as a copy.
        Assert.Contains("if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.Html))", Body(Images, "OnDragOver"), StringComparison.Ordinal);

        string drop = Body(Images, "OnDrop");
        Assert.Contains("NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition", drop, StringComparison.Ordinal);

        // A browser's picture has no path: copied first, then added as any file is, the copy removed after (#31).
        Assert.Contains("(IReadOnlyList<string> paths, string? copies) = await DroppedItems.PathsOf(e.DataView);", drop, StringComparison.Ordinal);
        Assert.Matches(@"_selection = TextSelection\.Caret\(at\);\s*try\s*\{\s*AddFiles\(paths\);\s*\}\s*finally\s*\{\s*DroppedItems\.Release\(copies\);", drop);

        // Text is read while the drop lasts, then pasted as Ctrl+V pastes it.
        Assert.Contains("PasteAsync(formatted: true, from: text.GetView());", drop, StringComparison.Ordinal);
        Assert.Matches(@"finally\s*\{\s*deferral\.Complete\(\);", drop);
    }

    [Fact]
    public void Pictures_are_drawn_over_the_editor_never_read_or_hit()
    {
        XElement marks = HostXaml.Root!.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ImageMarks");
        Assert.Equal("False", (string?)marks.Attribute("IsHitTestVisible"));
        Assert.Equal("Raw", (string?)marks.Attribute("AutomationProperties.AccessibilityView"));

        // Placed with the checkboxes, after the layout that follows each change; never with markup shown.
        Assert.Matches(@"TaskMarks\.Children\.Clear\(\);\s*PlaceImages\(\);", Body(Host, "PlaceTaskMarks"));
        string place = Body(Images, "PlaceImages");
        Assert.Matches(@"ImageMarks\.Children\.Clear\(\);\s*if \(_showMarkup \|\| Images is null\)\s*\{\s*return;", place);
        Assert.Contains("AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);", place, StringComparison.Ordinal);
        Assert.Contains("rect.Bottom - height - ImageGap + top", place, StringComparison.Ordinal);

        // The size is the store's file's and Core's fit; decoded at the drawn width, never at full resolution.
        string files = Code(Path.Join(Windows, "ImageFiles.cs"));
        Assert.Contains("DecodePixelType = DecodePixelType.Logical,", files, StringComparison.Ordinal);
        Assert.Contains("DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(width)),", files, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);", Body(files, "SizeOf"), StringComparison.Ordinal);
        Assert.Contains("ImageFiles.Bitmap(path, ImageSize.Fit(size.Width, size.Height, width, ImageMaxHeight).Width)", Body(Images, "Measure"), StringComparison.Ordinal);
        Assert.Contains("ImageSize.Fit(shown.Width, shown.Height, width, ImageMaxHeight)", Body(Images, "Drawn"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_draws_its_images_below_their_captions_which_a_reader_hears()
    {
        Assert.Contains("MarkdownReadView.ResolveImage = value is null ? null : value.PathOf;", MainImages, StringComparison.Ordinal);
        Assert.Contains("case MarkdownSpanKind.Quote or MarkdownSpanKind.Image:", ReadView, StringComparison.Ordinal);

        string picture = Body(ReadView, "Picture");
        Assert.Contains("AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);", picture, StringComparison.Ordinal);
        Assert.Contains("ImageSize.Fit(size.Width, size.Height, room, ImageMaxHeight)", picture, StringComparison.Ordinal);
        Assert.Contains("bitmap.ImageFailed += (_, _) => holder.Child = Notice(\"This image can't be shown\", markup);", picture, StringComparison.Ordinal);
        Assert.Contains("holder.Child = Notice(path is null ? \"Image not found\" : \"This image can't be shown\", markup);", picture, StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_is_given_to_the_editor_and_unreferenced_files_are_swept_off_the_ui_thread()
    {
        Assert.Contains("_ = Task.Run(() => images.SweepUnreferenced(database, DateTimeOffset.UtcNow));", App, StringComparison.Ordinal);
        Assert.Contains("Images = images,", App, StringComparison.Ordinal);
        Assert.Matches(@"_editor = host;\s*host\.Images = Images;\s*host\.Load\(", Main);
        Assert.Contains("host.ImageNotice += OnEditorImageNotice;", Main, StringComparison.Ordinal);
        Assert.Contains("private void OnEditorImageNotice(object? sender, string message) => ShowNoteNotice(message);", MainImages, StringComparison.Ordinal);
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)(?: static)?(?: async)? [\w<>?,\[\]() ]+? {method}\(");
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

        throw new InvalidOperationException($"Unbalanced body for {method}");
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
