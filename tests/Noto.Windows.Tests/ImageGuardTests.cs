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
    private static readonly string Preview = Code(Path.Join(Windows, "MainWindow.Preview.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));
    private static readonly XDocument HostXaml = XDocument.Load(Path.Join(Windows, "NoteEditorHost.xaml"));

    [Fact]
    public void A_picture_is_pasted_only_when_the_clipboard_has_no_files_or_text()
    {
        string paste = Body(Host, "PasteAsync");
        Assert.Matches(@"if \(asMarkdown && paths\.Count == 0 && plain is null && html is null && clipboard\.Contains\(StandardDataFormats\.Bitmap\)\)\s*\{\s*picture = await clipboard\.GetBitmapAsync\(\);", paste);
        Assert.Matches(@"if \(picture is not null\)\s*\{\s*await AddPicture\(picture\);\s*return;", paste);

        // Kept as PNG, whatever format it came in; a refusal is said, never silent.
        string picture = Body(Images, "AddPicture");
        Assert.Contains("Images.Keep(png, \"png\")", picture, StringComparison.Ordinal);
        Assert.Contains("Apply(MarkdownCommands.InsertImage(_text, _selection, reference, \"Pasted image\"), record: true);", picture, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(picture, @"ImageNotice\?\.Invoke\(this, Explain\("));
    }

    [Fact]
    public void Ctrl_Shift_P_picks_a_picture_owned_by_the_window_and_inserts_it_as_a_dropped_one()
    {
        // #194 (G35, D29): the picture command opens the picker; the picker can't be driven by the harness, so this is its check.
        Assert.Matches(@"case EditorCommand\.Picture:\s*PickPicture\(\);\s*return true;", Body(Host, "Run"));

        string pick = Body(Images, "PickPicture");

        // The types a drop and a paste keep, and the window handle an unpackaged app's picker needs.
        Assert.Matches(@"foreach \(string type in ImageReference\.Types\)\s*\{\s*picker\.FileTypeFilter\.Add\(""\."" \+ type\);", pick);
        Assert.Contains("InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));", pick, StringComparison.Ordinal);

        // Cancelling returns before anything is read back or changed; a chosen file goes the drop's way: one undo step.
        Assert.Matches(@"if \(file is null \|\| !Path\.IsPathFullyQualified\(file\.Path\)\)\s*\{\s*return;\s*\}\s*Flush\(\);\s*AddFiles\(\[file\.Path\]\);", pick);
        Assert.Single(Regex.Matches(pick, @"AddFiles\("));
        Assert.DoesNotContain("Apply(", pick, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_files_are_kept_as_copies_and_other_files_linked()
    {
        // What each file becomes is Core's answer from the Importing settings (#196): by default, as before.
        string files = Body(Images, "AddFiles");
        Assert.Contains("var chosen = paths.ToLookup(p => FileShortcuts.Choose(p, PictureDrops, TextFileDrops, Images is not null, copies is null || !p.StartsWith(copies, StringComparison.OrdinalIgnoreCase)));", files, StringComparison.Ordinal);
        Assert.Contains("var images = chosen[FileDrop.KeepPicture].ToList();", files, StringComparison.Ordinal);
        Assert.Contains("var others = chosen[FileDrop.InsertText].Where(p => !InsertTextOf(p)).Concat(chosen[FileDrop.Link]).ToList();", files, StringComparison.Ordinal);
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
        Assert.Matches(@"_selection = TextSelection\.Caret\(at\);\s*try\s*\{\s*AddFiles\(paths, copies\);\s*\}\s*finally\s*\{\s*DroppedItems\.Release\(copies\);", drop);

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
        Assert.Contains("rect.Bottom - after - height - ImageGap + top", place, StringComparison.Ordinal);   // the paragraph spacing below it (#234)

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

    [Fact]
    public void A_picture_is_copied_as_itself_from_the_editors_menu_and_from_a_selection_of_it()
    {
        // D32 (#187): wired once with the editor's events; the right button's press says where its menu opened.
        Assert.Matches(@"AttachToolbar\(\);\s*AttachPictureCopy\(\);", Host);
        string attach = Body(Images, "AttachPictureCopy");
        Assert.Contains("NoteEditor.AddHandler(PointerPressedEvent, new PointerEventHandler(", attach, StringComparison.Ordinal);
        Assert.Contains("pressed.Properties.IsRightButtonPressed", attach, StringComparison.Ordinal);
        Assert.Contains("NoteEditor.ContextFlyout.Opening += OnEditorMenuOpening;", attach, StringComparison.Ordinal);
        Assert.Contains("NoteEditor.CopyingToClipboard += OnCopyingToClipboard;", attach, StringComparison.Ordinal);

        // The menu offers it on a picture's line only (Core's ImageReference.At), at the press or else the caret.
        string opening = Body(Images, "OnEditorMenuOpening");
        Assert.Contains("_menuPoint is { } point ? NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition : _selection.Active", opening, StringComparison.Ordinal);
        Assert.Contains("_menuPicture = Images?.PathOf(ImageReference.At(_document, at));", opening, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(_copyPicture, \"Copy picture\");", opening, StringComparison.Ordinal);
        // The menu is shared by every text control: each editor takes its own item out, and only the target decides.
        Assert.Matches(@"_ = menu\.SecondaryCommands\.Remove\(_copyPicture\);\s*_ = menu\.SecondaryCommands\.Remove\(_savePicture\);\s*\}\s*if \(!ReferenceEquals\(menu\.Target, NoteEditor\)\)\s*\{\s*return;", opening);
        Assert.Matches(@"if \(_menuPicture is not null\)\s*\{\s*menu\.SecondaryCommands\.Insert\(0, _copyPicture\);", opening);

        // Ctrl+C or Copy with only the picture selected (Core's ImageReference.Selected): its source text and the picture.
        string copying = Body(Images, "OnCopyingToClipboard");
        Assert.Matches(@"ImageReference\.Selected\(_document, _selection\.Start, _selection\.End\)\) is \{ \} path\)\s*\{\s*e\.Handled = true;\s*CopyPicture\(path, source: _text\.Substring\(_selection\.Start, _selection\.Length\)\);", copying);

        // The kept file's own bytes, never re-encoded; the file too from the menu; a failure said, never silent.
        string copy = Body(Images, "CopyPicture");
        Assert.Contains("package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));", copy, StringComparison.Ordinal);
        // The image only: storage items do not reach desktop apps as a file drop (measured, Invoke-Images case 16).
        Assert.DoesNotContain("SetStorageItems", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("BitmapEncoder", copy, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetContent(package);", copy, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(copy, @"ImageNotice\?\.Invoke\(this, "));
    }

    [Fact]
    public void A_picture_is_saved_as_its_kept_file_from_the_same_menu_through_a_picker_owned_by_the_window()
    {
        // D33 (#235): beside Copy picture, on the same picture's line and the same menu target; the picker can't be
        // driven by the harness, so this is its check.
        string opening = Body(Images, "OnEditorMenuOpening");
        Assert.Contains("_menuPictureName = ImageReference.SaveNameAt(_document, at);", opening, StringComparison.Ordinal);
        Assert.Contains("_savePicture = new AppBarButton { Label = \"Save picture as…\", Icon = new SymbolIcon(Symbol.Save) };", opening, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(_savePicture, \"Save picture as\");", opening, StringComparison.Ordinal);
        Assert.Matches(@"if \(_menuPicture is \{ \} path\)\s*\{\s*SavePicture\(path, _menuPictureName\);", opening);
        Assert.Matches(@"menu\.SecondaryCommands\.Insert\(0, _copyPicture\);\s*menu\.SecondaryCommands\.Insert\(1, _savePicture\);", opening);

        // The caption as the name, the file's own type, the window's handle as an unpackaged app's picker needs; the kept
        // file copied byte for byte; cancelling does nothing; a failure said through the editor's image notice.
        string save = Body(Images, "SavePicture");
        Assert.Contains("SuggestedFileName = name ?? Path.GetFileNameWithoutExtension(path),", save, StringComparison.Ordinal);
        Assert.Contains("picker.FileTypeChoices.Add(type.TrimStart('.').ToUpperInvariant() + \" image\", [type]);", save, StringComparison.Ordinal);
        Assert.Contains("InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));", save, StringComparison.Ordinal);
        Assert.Matches(@"if \(await picker\.PickSaveFileAsync\(\) is \{ \} destination\)\s*\{\s*StorageFile picture = await StorageFile\.GetFileFromPathAsync\(path\);\s*await picture\.CopyAndReplaceAsync\(destination\);", save);
        Assert.DoesNotContain("BitmapEncoder", save, StringComparison.Ordinal);
        Assert.Contains("ImageNotice?.Invoke(this, \"The picture couldn't be saved.\");", save, StringComparison.Ordinal);
    }

    [Fact]
    public void Space_in_the_editor_previews_only_on_a_picture_and_is_typed_everywhere_else()
    {
        // #268 (D35, G44): which selection is "on a picture" is Core's (NotePictures.At, its tests); the editor hands Space
        // over only then. Elsewhere, with a modifier, or mid-composition it is not handled, so RichEdit types it.
        string keys = Body(Host, "OnPreviewKeyDown");
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Space && !ctrl && !alt && !shift && _composingFrom is null\)\s*\{\s*Flush\(\);\s*if \(NotePictures\.At\(_document, _selection\.Start, _selection\.End\) is \{ \} pictures\)\s*\{\s*e\.Handled = true;\s*PictureRequested\?\.Invoke\(this, pictures\);\s*\}\s*return;\s*\}", keys);

        Assert.Contains("host.PictureRequested += OnEditorPictureRequested;", Main, StringComparison.Ordinal);
        Assert.Contains("host.PictureRequested -= OnEditorPictureRequested;", Main, StringComparison.Ordinal);
        Assert.Contains("private void OnEditorPictureRequested(object? sender, NotePictures pictures) => OpenPreview(pictures);", Preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Space_on_a_card_previews_only_a_card_with_pictures()
    {
        // A card without pictures, or Space from the editor inside a card, is left to the list and the editor.
        string keys = Body(Main, "OnNoteListPreviewKeyDown");
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Space && !IsControlDown\(\) && !IsAltDown\(\) && !IsShiftDown\(\)\s*&& e\.OriginalSource is ListViewItem \{ Content: NoteCard pictured \}\s*&& NotePictures\.Of\(MarkdownDocument\.Parse\(pictured\.Content, pictured\.Mode\)\) is \{ \} pictures\)\s*\{\s*e\.Handled = true;\s*OpenPreview\(pictures\);\s*return;", keys);
    }

    [Fact]
    public void The_preview_is_a_dialog_that_keeps_the_keyboard_and_gives_focus_back()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement surface = MainXaml.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "PreviewSurface");
        Assert.Equal("PicturePreview", surface.Name.LocalName);
        Assert.Equal("2", (string?)surface.Attribute("Grid.RowSpan"));   // over the whole drawer, the header too
        Assert.Equal("True", (string?)surface.Attribute("IsTabStop"));
        Assert.Equal("PicturePreview", (string?)surface.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("OnPictureKeyDown", (string?)surface.Attribute("KeyDown"));
        Assert.Equal("OnPictureKeyUp", (string?)surface.Attribute("KeyUp"));
        Assert.Equal("OnPictureAccelerators", (string?)surface.Attribute("ProcessKeyboardAccelerators"));
        Assert.Equal("{ThemeResource SurfaceScrimBrush}", (string?)surface.Elements().First().Attribute("Background"));
        XElement status = surface.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "PreviewStatus");
        Assert.Equal("PicturePreviewStatus", (string?)status.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("Polite", (string?)status.Attribute("AutomationProperties.LiveSetting"));
        Assert.Contains("protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);", Preview, StringComparison.Ordinal);

        // Named for its picture, the position with it; Left and Right step through Core's list, which stops at the ends.
        string show = Body(Preview, "ShowPicture");
        Assert.Contains("AutomationProperties.SetName(PreviewSurface, picture.Name);", show, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(PreviewSurface, pictures.Position);", show, StringComparison.Ordinal);
        Assert.Contains("PreviewStatus.Text = pictures.Position;", show, StringComparison.Ordinal);
        // Every key is the preview's, so Tab never leaves it and no chord acts behind it. The keys are acted on where
        // they are first seen: a handled ProcessKeyboardAccelerators means WinUI raises no KeyDown (Invoke-Images case
        // 18's first run: Right and Escape reached only the accelerator handler).
        Assert.Contains("private void OnPictureKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = true;", Preview, StringComparison.Ordinal);
        Assert.Contains("NotePictures next = pictures.Step(key == VirtualKey.Left ? -1 : 1);", Body(Preview, "PreviewKey"), StringComparison.Ordinal);
        Assert.Matches(@"args\.Handled = true;\s*if \(args\.Modifiers == VirtualKeyModifiers\.None\)\s*\{\s*PreviewKey\(args\.Key\);", Body(Preview, "OnPictureAccelerators"));

        // Focus goes in once laid out, and back to exactly what had it: the card, or the editor with its caret. Closing
        // waits for Escape or Space to come up, so the key's character never types into the editor it returns to.
        string open = Body(Preview, "OpenPreview");
        Assert.Contains("_previewReturnFocus = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;", open, StringComparison.Ordinal);
        Assert.Contains("_ = PreviewSurface.Focus(FocusState.Programmatic);", open, StringComparison.Ordinal);
        Assert.Matches(@"_previewClosing && e\.Key is VirtualKey\.Escape or VirtualKey\.Space\)\s*\{\s*ClosePreview\(\);", Body(Preview, "OnPictureKeyUp"));
        string close = Body(Preview, "ClosePreview");
        Assert.Matches(@"!back\.Focus\(FocusState\.Programmatic\)\)\s*\{\s*MoveFocusToNotes\(\);\s*\}\s*PreviewSurface\.Visibility = Visibility\.Collapsed;\s*PreviewImage\.Source = null;", close);
        Assert.Matches(@"SettingsSurface\?\.CancelRecording\(\);\s*ClosePreview\(\);", Main);
    }

    [Fact]
    public void The_preview_decodes_off_the_ui_thread_at_a_capped_size_and_never_fetches_or_logs_a_picture()
    {
        Assert.Contains("internal const int PreviewDecodeCap = 4096;", Preview, StringComparison.Ordinal);
        string width = Body(Preview, "PreviewWidth");
        Assert.Contains("ImageSize.Fit(width, height, WorkspaceContent.ActualWidth, WorkspaceContent.ActualHeight)", width, StringComparison.Ordinal);
        Assert.Contains("return Math.Min(fitted, PreviewDecodeCap / scale);", width, StringComparison.Ordinal);

        // Only a file Noto keeps: a web picture never resolves, so it is never fetched; it and an unreadable one say so.
        string show = Body(Preview, "ShowPicture");
        Assert.Contains("string? path = Images?.PathOf(picture.Source);", show, StringComparison.Ordinal);
        Assert.Contains("await ImageFiles.SizeOf(path)", show, StringComparison.Ordinal);
        Assert.Contains("BitmapImage bitmap = ImageFiles.Bitmap(path, PreviewWidth(pixels.Width, pixels.Height));", show, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(show, @"ShowPictureNotice\(\);"));
        Assert.Contains("PreviewNoticeText.Text = \"This picture can't be shown\";", Preview, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Http|WebRequest|Uri\(", Preview);

        // Where, never what: no path and no caption in a trace.
        var traces = Regex.Matches(Preview, @"Trace\.WriteLine\(.*\);").Select(m => m.Value).ToList();
        Assert.Equal(2, traces.Count);
        Assert.Contains(traces, t => t.Contains("Noto.Preview shown index={pictures.Index + 1} count={pictures.All.Count}", StringComparison.Ordinal));
        Assert.Contains(traces, t => t.Contains("\"Noto.Preview closed\"", StringComparison.Ordinal));
        Assert.All(traces, t => Assert.DoesNotMatch("path|Name|Caption|Source", t));
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
