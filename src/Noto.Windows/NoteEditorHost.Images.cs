using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Noto.Core.Editing;
using Noto.Core.Images;
using Noto.Core.Links;
using Noto.Core.Markdown;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Noto;

/// <summary>
/// Images in the editor (ADR-019 §4). The source stays the text: an image's line keeps <c>![name](reference)</c>, its
/// name shown as a caption, and the line is given space below in which an overlay draws the image, as the checkbox
/// overlay draws boxes. Nothing enters the document that is not in the source.
/// </summary>
public sealed partial class NoteEditorHost
{
    /// <summary>More images than this in one note are left as their captions: the overlay is per image.</summary>
    private const int ImageLimit = 50;

    /// <summary>The tallest an image is drawn; wider ones fit the editor's width.</summary>
    private const double ImageMaxHeight = 320;

    /// <summary>Space above and below a drawn image, inside its line's space.</summary>
    private const double ImageGap = 6;

    /// <summary>The height a missing or unreadable image's notice takes.</summary>
    private const double ImageNoticeHeight = 20;

    /// <summary>Each image file seen, by path: <see langword="null"/> while its size is read.</summary>
    private readonly Dictionary<string, Shown?> _images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the note shown was loaded, until its images are all drawn: timing for the validation channel.</summary>
    private long _imagesFrom;

    /// <summary>The images of the last placing still being read.</summary>
    private int _imagesPending;

    /// <summary>The image files beside the notes; set by the window.</summary>
    internal IImageStore? Images { get; set; }

    /// <summary>Something about an image could not be done: what to tell the user.</summary>
    internal event EventHandler<string>? ImageNotice;

    /// <summary>
    /// Each image of the note drawn below its line, the line given exactly the space it needs. Where a line's space
    /// changes, the images are placed again once that is laid out, so every one sits below its own line.
    /// </summary>
    private void PlaceImages()
    {
        ImageMarks.Children.Clear();

        if (_showMarkup || Images is null)
        {
            return;
        }

        var images = _document.Blocks
            .SelectMany(b => b.Spans.Where(s => s.Kind == MarkdownSpanKind.Image).Select(s => (Block: b, Image: s)))
            .Take(ImageLimit + 1)
            .ToList();

        if (images.Count == 0 || images.Count > ImageLimit)
        {
            _imagesFrom = 0;
            return;
        }

        // Not laid out yet: the editor's size change places them.
        double width = NoteEditor.ActualWidth - NoteEditor.BorderThickness.Left - NoteEditor.BorderThickness.Right - NoteEditor.Padding.Left - NoteEditor.Padding.Right;
        if (width <= 0)
        {
            return;
        }

        bool spaceChanged = false;
        _imagesPending = 0;
        var placed = new List<(FrameworkElement Element, double Height, int LineEnd)>();

        foreach ((MarkdownBlock block, MarkdownSpan image) in images)
        {
            (FrameworkElement element, double height) = Drawn(ReferenceOf(block, image), width);

            ITextRange line = NoteEditor.Document.GetRange(block.Start, block.End);
            float space = (float)((height + (2 * ImageGap)) * 0.75);
            if (Math.Abs(line.ParagraphFormat.SpaceAfter - space) > 0.5f)
            {
                Set(() => line.ParagraphFormat.SpaceAfter = space);
                spaceChanged = true;
            }

            placed.Add((element, height, block.End));
        }

        if (spaceChanged)
        {
            QueueTaskMarks();
            return;
        }

        // RichEdit measures from inside the control's border and padding, as the checkbox overlay does.
        var toMarks = NoteEditor.TransformToVisual(EditorRoot);
        double left = NoteEditor.BorderThickness.Left + NoteEditor.Padding.Left;
        double top = NoteEditor.BorderThickness.Top + NoteEditor.Padding.Top;

        foreach ((FrameworkElement element, double height, int lineEnd) in placed)
        {
            // A line's rectangle takes in the space below it (measured): the picture sits at the top of that space.
            NoteEditor.Document.GetRange(Math.Max(0, lineEnd - 1), lineEnd).GetRect(PointOptions.ClientCoordinates, out Windows.Foundation.Rect rect, out _);
            Windows.Foundation.Point at = toMarks.TransformPoint(new Windows.Foundation.Point(left, rect.Bottom - height - ImageGap + top));
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
            Canvas.SetLeft(element, at.X);
            Canvas.SetTop(element, at.Y);
            ImageMarks.Children.Add(element);
        }

        // What the user waits for: every image of the note drawn after it was loaded. Timing only, never content.
        if (_imagesFrom != 0 && _imagesPending == 0)
        {
            Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Editor images drawn count={placed.Count} ms={Stopwatch.GetElapsedTime(_imagesFrom).TotalMilliseconds:F2}"));
            _imagesFrom = 0;
        }
    }

    /// <summary>The image's source as written, if it has one.</summary>
    private string? ReferenceOf(MarkdownBlock block, MarkdownSpan image) =>
        block.Spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start > image.Start && s.End < image.End)
            .Select(s => _text.Substring(s.Start, s.Length))
            .LastOrDefault();

    /// <summary>
    /// What is drawn for an image and how tall: the picture, at most the editor's width and <see cref="ImageMaxHeight"/>
    /// tall, never enlarged; or a one-line notice when its file is not there or cannot be read.
    /// </summary>
    private (FrameworkElement Element, double Height) Drawn(string? reference, double width)
    {
        string? path = Images?.PathOf(reference);
        if (path is null)
        {
            return (Notice("Image not found"), ImageNoticeHeight);
        }

        // Its size is read from the file's header first, off the layout; until then it takes no room.
        if (!_images.TryGetValue(path, out Shown? shown))
        {
            _images[path] = null;
            _ = Measure(path, width);
        }

        if (shown is null)
        {
            _imagesPending++;
            return (new Border(), 0);
        }

        if (shown.Bitmap is null)
        {
            return (Notice("This image can't be shown"), ImageNoticeHeight);
        }

        (double drawnWidth, double drawnHeight) = ImageSize.Fit(shown.Width, shown.Height, width, ImageMaxHeight);
        return (ImageFiles.Picture(shown.Bitmap, drawnWidth, drawnHeight), drawnHeight);
    }

    /// <summary>Reads the image's size and starts decoding it at the width it is drawn; then the images are placed again.</summary>
    private async Task Measure(string path, double width)
    {
        Shown unreadable = new(null, 0, 0);
        Shown shown = unreadable;

        if (await ImageFiles.SizeOf(path) is { } size)
        {
            BitmapImage bitmap = ImageFiles.Bitmap(path, ImageSize.Fit(size.Width, size.Height, width, ImageMaxHeight).Width);
            bitmap.ImageFailed += (_, _) =>
            {
                _images[path] = unreadable;
                QueueTaskMarks();
            };
            shown = new Shown(bitmap, size.Width, size.Height);
        }

        _images[path] = shown;
        QueueTaskMarks();
    }

    /// <summary>A kept image as the editor draws it: decoded, and its size in pixels; no bitmap when it can't be read.</summary>
    private sealed record Shown(BitmapImage? Bitmap, int Width, int Height);

    private TextBlock Notice(string text) => new()
    {
        Text = text,
        FontStyle = Windows.UI.Text.FontStyle.Italic,
        FontSize = (double)Application.Current.Resources["FontSizeCaption"],
        Foreground = NoteEditor.MarkupBrush,
        Height = ImageNoticeHeight,
    };

    /// <summary>
    /// Files given to the editor (pasted after Explorer's Copy, or dropped), at the caret, each as the Importing settings
    /// say (J30, #196; by default as before): a text file's text inserted, if asked for; anything not kept linked as a
    /// shortcut on a line of its own (D34); then each image kept beside the notes and shown on a line of its own.
    /// </summary>
    /// <param name="paths">The files' paths.</param>
    /// <param name="copies">The folder of a drop's copies of files with no path of their own, removed after the drop.</param>
    private void AddFiles(IReadOnlyList<string> paths, string? copies = null)
    {
        var chosen = paths.ToLookup(p => FileShortcuts.Choose(p, PictureDrops, TextFileDrops, Images is not null, copies is null || !p.StartsWith(copies, StringComparison.OrdinalIgnoreCase)));
        var images = chosen[FileDrop.KeepPicture].ToList();

        // A text file that can't be read as text is linked instead.
        var others = chosen[FileDrop.InsertText].Where(p => !InsertTextOf(p)).Concat(chosen[FileDrop.Link]).ToList();

        if (others.Count > 0)
        {
            Apply(MarkdownCommands.LinkPaths(_text, _selection, others), record: true);
        }

        foreach (string path in images)
        {
            (ImageKeeping outcome, string? reference) = Images!.KeepCopyOf(path);
            if (outcome == ImageKeeping.Kept && reference is not null)
            {
                Apply(MarkdownCommands.InsertImage(_text, _selection, reference, Path.GetFileName(path)), record: true);
            }
            else
            {
                ImageNotice?.Invoke(this, Explain(outcome));
            }
        }
    }

    /// <summary>
    /// Ctrl+Shift+P (G35, D29, #194): a picture chosen in Windows' picker, of the types a drop or a paste keeps, kept
    /// and inserted at the caret as a dropped one is: one undo step. Cancelling changes nothing. The picker belongs to
    /// this editor's window, as an unpackaged app's pickers must (as Settings' backup pickers do).
    /// </summary>
    private async void PickPicture()
    {
        if (Images is null || XamlRoot is null)
        {
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (string type in ImageReference.Types)
        {
            picker.FileTypeFilter.Add("." + type);
        }

        InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));

        StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (COMException)
        {
            ImageNotice?.Invoke(this, "The picture couldn't be chosen.");
            return;
        }

        // Cancelled, or a file with no path of its own (one only a provider app holds): nothing to keep a copy of.
        if (file is null || !Path.IsPathFullyQualified(file.Path))
        {
            return;
        }

        Flush();
        AddFiles([file.Path]);
        _ = NoteEditor.Focus(FocusState.Programmatic);
    }

    /// <summary>A picture on the clipboard (a screenshot, a copied image), kept as PNG and inserted at the caret.</summary>
    private async Task AddPicture(RandomAccessStreamReference picture)
    {
        if (Images is null)
        {
            return;
        }

        byte[]? png = await PngOf(picture);
        if (png is null)
        {
            ImageNotice?.Invoke(this, Explain(ImageKeeping.NotAnImage));
            return;
        }

        (ImageKeeping outcome, string? reference) = Images.Keep(png, "png");
        if (outcome != ImageKeeping.Kept || reference is null)
        {
            ImageNotice?.Invoke(this, Explain(outcome));
            return;
        }

        Flush();
        Apply(MarkdownCommands.InsertImage(_text, _selection, reference, "Pasted image"), record: true);
    }

    /// <summary>
    /// Copy picture (D32, #187): the editor's context menu offers it on a picture's line, and Ctrl+C (or the menu's
    /// Copy) with only a picture selected copies the picture too. Called once the editor's events are wired.
    /// </summary>
    /// <remarks>
    /// A picture is drawn over the space below its line and takes no input, so a right-click on it reaches the text
    /// there: that line. RichEdit opens its menu before any handler of the request runs, so the right button's press,
    /// which comes first, says where; Shift+F10, the Menu key and a touch hold use the caret.
    /// </remarks>
    private void AttachPictureCopy()
    {
        NoteEditor.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            Microsoft.UI.Input.PointerPoint pressed = e.GetCurrentPoint(NoteEditor);
            _menuPoint = pressed.Properties.IsRightButtonPressed
                ? new Windows.Foundation.Point(
                    pressed.Position.X - NoteEditor.BorderThickness.Left - NoteEditor.Padding.Left,
                    pressed.Position.Y - NoteEditor.BorderThickness.Top - NoteEditor.Padding.Top + ScrolledBy)
                : null;
        }), handledEventsToo: true);

        NoteEditor.ContextFlyout ??= new TextCommandBarFlyout();
        NoteEditor.ContextFlyout.Opening += OnEditorMenuOpening;
        NoteEditor.CopyingToClipboard += OnCopyingToClipboard;
    }

    // Where the right button was last pressed in the text, in RichEdit's client coordinates, until its menu reads it.
    private Windows.Foundation.Point? _menuPoint;

    // The menu's Copy picture and Save picture as, made once; in the menu only while it opens on a picture's line.
    private AppBarButton? _copyPicture;
    private AppBarButton? _savePicture;

    // The file of the picture the menu opened on, and the name Save picture as suggests for it.
    private string? _menuPicture;
    private string? _menuPictureName;

    /// <summary>
    /// The editor's menu opens: Copy picture (D32) and Save picture as (D33, #235) are in it when it opens on a picture's line.
    /// </summary>
    /// <remarks>
    /// Text controls share one built-in menu, so every editor's handler runs whenever it opens (measured: Copy picture
    /// was offered for another note's picture, Invoke-Images case 16). Each editor takes its own item out, and only the
    /// one the menu opens for (its <c>Target</c>) decides.
    /// </remarks>
    private void OnEditorMenuOpening(object? sender, object e)
    {
        if (sender is not CommandBarFlyout menu)
        {
            return;
        }

        if (_copyPicture is not null)
        {
            _ = menu.SecondaryCommands.Remove(_copyPicture);
            _ = menu.SecondaryCommands.Remove(_savePicture);
        }

        if (!ReferenceEquals(menu.Target, NoteEditor))
        {
            return;
        }

        Flush();
        int at = _menuPoint is { } point ? NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition : _selection.Active;
        _menuPoint = null;
        _menuPicture = Images?.PathOf(ImageReference.At(_document, at));
        _menuPictureName = ImageReference.SaveNameAt(_document, at);

        if (_copyPicture is null)
        {
            _copyPicture = new AppBarButton { Label = "Copy picture", Icon = new SymbolIcon(Symbol.Copy) };
            AutomationProperties.SetName(_copyPicture, "Copy picture");
            _copyPicture.Click += (_, _) =>
            {
                if (_menuPicture is { } path)
                {
                    CopyPicture(path, source: null);
                }
            };

            _savePicture = new AppBarButton { Label = "Save picture as…", Icon = new SymbolIcon(Symbol.Save) };
            AutomationProperties.SetName(_savePicture, "Save picture as");
            _savePicture.Click += (_, _) =>
            {
                if (_menuPicture is { } path)
                {
                    SavePicture(path, _menuPictureName);
                }
            };
        }

        if (_menuPicture is not null)
        {
            menu.SecondaryCommands.Insert(0, _copyPicture);
            menu.SecondaryCommands.Insert(1, _savePicture);
        }
    }

    /// <summary>
    /// Save picture as (D33, #235): Windows' save picker, owned by this editor's window as the backup pickers are, offers
    /// the picture's caption as the name and its own type; the kept file is copied there byte for byte, never re-encoded.
    /// Cancelling does nothing.
    /// </summary>
    private async void SavePicture(string path, string? name)
    {
        if (XamlRoot is null)
        {
            return;
        }

        string type = Path.GetExtension(path);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = name ?? Path.GetFileNameWithoutExtension(path),
        };
        picker.FileTypeChoices.Add(type.TrimStart('.').ToUpperInvariant() + " image", [type]);
        InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));

        try
        {
            if (await picker.PickSaveFileAsync() is { } destination)
            {
                StorageFile picture = await StorageFile.GetFileFromPathAsync(path);
                await picture.CopyAndReplaceAsync(destination);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            ImageNotice?.Invoke(this, "The picture couldn't be saved.");
        }
    }

    /// <summary>
    /// Ctrl+C, or the menu's Copy, with only a picture selected (its caption whole, D32): its source as text, as any copy
    /// of the text is, so a paste in a note shows it again, and the picture itself for every other app.
    /// </summary>
    private void OnCopyingToClipboard(RichEditBox sender, TextControlCopyingToClipboardEventArgs e)
    {
        Flush();
        if (Images?.PathOf(ImageReference.Selected(_document, _selection.Start, _selection.End)) is { } path)
        {
            e.Handled = true;
            CopyPicture(path, source: _text.Substring(_selection.Start, _selection.Length));
        }
    }

    /// <summary>
    /// Puts a kept picture on the clipboard as itself (D32, #187): the file's own bytes as the bitmap, never re-encoded;
    /// and from a selection its source text too. Not the file: WinRT storage items do not reach desktop apps as a file drop.
    /// </summary>
    private async void CopyPicture(string path, string? source)
    {
        var package = new DataPackage();
        if (source is not null)
        {
            package.SetText(source);
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            // The text is still copied; the menu's copy, which has nothing else, says so.
            if (source is null)
            {
                ImageNotice?.Invoke(this, "The picture couldn't be copied.");
                return;
            }
        }

        try
        {
            Clipboard.SetContent(package);
            Trace.WriteLine($"Noto.Editor picture copied text={source is not null}");
        }
        catch (COMException)
        {
            ImageNotice?.Invoke(this, "Nothing was copied: another app is using the clipboard. Try again.");
        }
    }

    private static string Explain(ImageKeeping outcome) => outcome switch
    {
        ImageKeeping.TooLarge => "That image is larger than 25 MB, so it wasn't added.",
        ImageKeeping.NotAnImage => "That isn't an image Noto can show (PNG, JPEG, GIF, WebP or BMP).",
        _ => "The image couldn't be saved.",
    };

    /// <summary>The picture re-encoded as PNG; <see langword="null"/> when it cannot be decoded.</summary>
    private static async Task<byte[]?> PngOf(RandomAccessStreamReference picture)
    {
        try
        {
            using IRandomAccessStreamWithContentType source = await picture.OpenReadAsync();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            using var target = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            var png = new byte[target.Size];
            using var reader = new DataReader(target.GetInputStreamAt(0));
            await reader.LoadAsync((uint)target.Size);
            reader.ReadBytes(png);
            return png;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Files, and text from another app (#31), dragged over the editor are accepted as a copy.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.Html))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to note";
            e.Handled = true;
        }
    }

    /// <summary>
    /// Dropped files, or text, go where they were dropped: the caret is put at the drop point first. Text is pasted as
    /// Ctrl+V pastes it (a page's HTML as Markdown), one undo step (#31: Edge's text drag offers Text and HTML).
    /// </summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        bool files = e.DataView.Contains(StandardDataFormats.StorageItems);
        if (!files && !e.DataView.Contains(StandardDataFormats.Text) && !e.DataView.Contains(StandardDataFormats.Html))
        {
            return;
        }

        e.Handled = true;
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            var point = e.GetPosition(NoteEditor);
            point = new Windows.Foundation.Point(
                point.X - NoteEditor.BorderThickness.Left - NoteEditor.Padding.Left,
                point.Y - NoteEditor.BorderThickness.Top - NoteEditor.Padding.Top + ScrolledBy);
            int at = NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition;

            if (!files)
            {
                // Read now, while the drop lasts, into a package of Noto's own that the paste reads after.
                var text = new DataPackage();
                if (e.DataView.Contains(StandardDataFormats.Html))
                {
                    text.SetHtmlFormat(await e.DataView.GetHtmlFormatAsync());
                }

                if (e.DataView.Contains(StandardDataFormats.Text))
                {
                    text.SetText(await e.DataView.GetTextAsync());
                }

                Flush();
                Set(() => NoteEditor.Document.Selection.SetRange(at, at));
                _selection = TextSelection.Caret(at);
                PasteAsync(formatted: true, from: text.GetView());
                _ = NoteEditor.Focus(FocusState.Programmatic);
                return;
            }

            (IReadOnlyList<string> paths, string? copies) = await DroppedItems.PathsOf(e.DataView);

            Flush();
            Set(() => NoteEditor.Document.Selection.SetRange(at, at));
            _selection = TextSelection.Caret(at);
            try
            {
                AddFiles(paths, copies);
            }
            finally
            {
                DroppedItems.Release(copies);
            }

            _ = NoteEditor.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ImageNotice?.Invoke(this, "Those files couldn't be read.");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
