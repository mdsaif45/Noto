using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Noto.Core.Editing;
using Noto.Core.Images;
using Noto.Core.Markdown;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
        var toMarks = NoteEditor.TransformToVisual(ImageMarks);
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
    /// Files given to the editor (pasted after Explorer's Copy, or dropped), at the caret: anything that is not an image
    /// linked (D34), then each image kept beside the notes and shown on a line of its own.
    /// </summary>
    private void AddFiles(IReadOnlyList<string> paths)
    {
        var images = paths.Where(p => Images is not null && ImageReference.TypeOf(p) is not null).ToList();
        var others = paths.Except(images).ToList();

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

    /// <summary>Files dragged over the editor are accepted as a copy; anything else is RichEdit's.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to note";
            e.Handled = true;
        }
    }

    /// <summary>Dropped files go where they were dropped: the caret is put at the drop point first.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
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
                point.Y - NoteEditor.BorderThickness.Top - NoteEditor.Padding.Top);
            int at = NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition;

            IReadOnlyList<string> paths = [.. (await e.DataView.GetStorageItemsAsync()).Select(i => i.Path).Where(Path.IsPathFullyQualified)];

            Flush();
            Set(() => NoteEditor.Document.Selection.SetRange(at, at));
            _selection = TextSelection.Caret(at);
            AddFiles(paths);
            _ = NoteEditor.Focus(FocusState.Programmatic);
        }
        catch (COMException)
        {
            ImageNotice?.Invoke(this, "Those files couldn't be read.");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
