using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Noto.Core.Images;
using Noto.Core.Notes;
using Noto.Core.Portability;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Noto;

/// <summary>
/// A note saved or copied as a picture (#254; parity I4, J31 to J34): the card's menu renders the note as its read view
/// draws it, whole, in its colour and the drawer's theme, then saves it as a PNG through Windows' save dialog or puts it
/// on the clipboard. Settings › Export pictures sets its background, margins, shape and footer.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How a note picture is made (#254): set by the application from the stored settings, and on each change.</summary>
    internal PictureExport PictureExport { get; set; } = new(PictureBackground.NoteColor, PictureMargins.Small, PictureShape.Fit, false);

    // One picture at a time: a second request while one is made is ignored.
    private bool _picturing;

    /// <summary>
    /// Save as picture… (#254): the note rendered first, then Windows' save dialog, owned by this window as the backup
    /// pickers are (so the drawer stays while it is up); the PNG is written beside the chosen file and moved over it, so
    /// a failed save leaves no partial file. Cancelling does nothing.
    /// </summary>
    private async void SaveCardPicture(string id)
    {
        if (_picturing || CardPictureOf(id) is not { } request)
        {
            return;
        }

        _picturing = true;
        try
        {
            if (await RenderPicture(request) is not { } png)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = PortableNames.Safe(request.Title),
            };
            picker.FileTypeChoices.Add("PNG image", [".png"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            if (await picker.PickSaveFileAsync() is not { } file)
            {
                return;
            }

            if (!WritePicture(file.Path, png))
            {
                ShowNoteNotice("The picture couldn't be saved there.");
                return;
            }

            ShowNoteStatus($"Saved {file.Name}.");
            Trace.WriteLine($"Noto.Picture saved bytes={png.Length}");
        }
        catch (COMException)
        {
            ShowNoteNotice("The picture couldn't be saved.");
        }
        finally
        {
            _picturing = false;
        }
    }

    /// <summary>Copy as picture (#254): the note rendered, then put on the clipboard as a bitmap, held there after Noto quits.</summary>
    private async void CopyCardPicture(string id)
    {
        if (_picturing || CardPictureOf(id) is not { } request)
        {
            return;
        }

        _picturing = true;
        try
        {
            if (await RenderPicture(request) is not { } png)
            {
                return;
            }

            // Flushed, the clipboard holds its own copy, so the stream is not needed once this returns.
            using var stream = new InMemoryRandomAccessStream();
            _ = await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));

            try
            {
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch (COMException)
            {
                ShowNoteNotice("Nothing was copied: another app is using the clipboard. Try again.");
                return;
            }

            ShowNoteStatus("Picture copied.");
            Trace.WriteLine($"Noto.Picture copied bytes={png.Length}");
        }
        finally
        {
            _picturing = false;
        }
    }

    /// <summary>What a card's picture shows, or <see langword="null"/> when there is no such card.</summary>
    /// <remarks>
    /// The text on screen: the card being edited gives its draft, saved or not, so nothing is written to make a picture.
    /// A folded card gives its whole note, not its title. Its width is the card's, as laid out now.
    /// </remarks>
    private CardPicture? CardPictureOf(string id)
    {
        if (_noteState != NoteState.Loaded
            || Notes.FirstOrDefault(n => n.Id == id) is not { } card
            || _stack.Notes.FirstOrDefault(n => n.Id.Value == id) is not { } note)
        {
            return null;
        }

        bool editing = _stack.Editing?.Value == id;
        if (editing)
        {
            _editor?.ReadBack();
        }

        double width = NoteList.ContainerFromItem(card) is ListViewItem container
            ? container.ActualWidth - container.Padding.Left - container.Padding.Right
            : NoteList.ActualWidth;
        DateTimeOffset modified = editing && _stack.IsDirty ? DateTimeOffset.Now : note.UpdatedAt;

        return new CardPicture(
            editing ? _stack.Draft : card.Content,
            card.Mode,
            card.ColorKey,
            card.DisplayTitle,
            modified,
            width > 0 ? width : NoteList.ActualWidth);
    }

    /// <summary>
    /// The note as a PNG, or <see langword="null"/> when it could not be made (the notice says so). Built from
    /// NotePictureTemplate at the card's width and font, under the drawer so it takes the user's theme, on a canvas out
    /// of view; laid out whole, its pictures decoded before it is rendered, then sized to the chosen margins and shape
    /// (<see cref="NotePicture.Layout"/>) and rendered at the screen's scale, so its text is as crisp as on screen.
    /// </summary>
    private async Task<byte[]?> RenderPicture(CardPicture request)
    {
        PictureExport export = PictureExport;
        var root = (Grid)((DataTemplate)WorkspaceContent.Resources["NotePictureTemplate"]).LoadContent();
        var fill = (UIElement)root.Children[0];
        var tints = (Grid)root.Children[1];
        var note = (StackPanel)root.Children[2];
        var view = (MarkdownReadView)note.Children[0];
        var footer = (Grid)note.Children[1];

        bool coloured = export.Background == PictureBackground.NoteColor;
        int tint = request.ColorKey is null ? -1 : NoteColor.Keys.ToList().IndexOf(request.ColorKey);
        fill.Visibility = coloured ? Visibility.Visible : Visibility.Collapsed;
        if (coloured && tint >= 0)
        {
            tints.Children[tint].Visibility = Visibility.Visible;
        }

        view.Width = Math.Max(1, request.Width);
        if (_noteFont is not null)
        {
            view.FontFamily = _noteFont;
        }

        view.FontSize = _noteSize;
        view.Mode = request.Mode;
        view.Source = request.Content;
        footer.Visibility = export.Footer ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)footer.Children[0]).Text = request.Modified.ToLocalTime().ToString("D", CultureInfo.CurrentCulture);

        // Out of view, never clipped: a Canvas measures its children unbounded, so a long note is laid out whole.
        var stage = new Canvas { IsHitTestVisible = false };
        Grid.SetRowSpan(stage, 2);
        Canvas.SetLeft(root, -NotePicture.MaxSide);
        stage.Children.Add(root);
        WorkspaceContent.Children.Add(stage);
        long started = Stopwatch.GetTimestamp();

        try
        {
            root.UpdateLayout();

            // A picture still decoding renders blank. One that never answers is not waited for forever.
            try
            {
                await view.Drawn.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                Trace.WriteLine("Noto.Picture pictures not drawn within 5 s");
            }

            root.UpdateLayout();
            PictureLayout layout = NotePicture.Layout(note.DesiredSize.Width, Math.Max(1, note.DesiredSize.Height), export.Margins, export.Shape);
            root.Width = layout.Width;
            root.Height = layout.Height;
            note.Margin = new Thickness(layout.Left, layout.Top, 0, 0);
            root.UpdateLayout();

            double screen = Content.XamlRoot.RasterizationScale;
            double scale = NotePicture.ScaleWithin(layout.Width, layout.Height, screen);
            var bitmap = new RenderTargetBitmap();
            if (scale < screen)
            {
                await bitmap.RenderAsync(root, (int)(layout.Width * scale), (int)(layout.Height * scale));
            }
            else
            {
                await bitmap.RenderAsync(root);
            }

            IBuffer pixels = await bitmap.GetPixelsAsync();
            using var png = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
            double dpi = 96 * scale;
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels.ToArray());
            await encoder.FlushAsync();

            var bytes = new byte[png.Size];
            using var reader = new DataReader(png.GetInputStreamAt(0));
            _ = await reader.LoadAsync((uint)png.Size);
            reader.ReadBytes(bytes);

            // Sizes and timing only, never the note's text.
            Trace.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Noto.Picture rendered pixels={bitmap.PixelWidth}x{bitmap.PixelHeight} scale={scale:F2} background={export.Background} margins={export.Margins} shape={export.Shape} footer={export.Footer} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}"));
            return bytes;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            Trace.WriteLine($"Noto.Picture failed error={ex.GetType().Name}");
            ShowNoteNotice("The picture couldn't be made.");
            return null;
        }
        finally
        {
            _ = WorkspaceContent.Children.Remove(stage);
        }
    }

    /// <summary>
    /// Writes <paramref name="png"/> to <paramref name="path"/> whole or not at all: into a file beside it, then moved
    /// over it. On failure that file is removed, and so is the empty file the save dialog made, never one the user chose
    /// to replace.
    /// </summary>
    /// <returns>Whether the picture is there.</returns>
    private static bool WritePicture(string path, byte[] png)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string partial = path + ".partial";
        try
        {
            File.WriteAllBytes(partial, png);
            File.Move(partial, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Noto.Picture save failed error={ex.GetType().Name}");
            DataPortability.RemoveEmpty(path);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(partial);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a file named for the picture, beside it.
            }
        }
    }

    /// <summary>What one card's picture is made from (#254).</summary>
    private sealed record CardPicture(string Content, NoteMode Mode, string? ColorKey, string Title, DateTimeOffset Modified, double Width);
}
