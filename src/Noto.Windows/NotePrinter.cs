using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Printing;
using Noto.Core.Markdown;
using Noto.Core.Notes;
using Noto.Core.Printing;
using Windows.Graphics.Imaging;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Noto;

/// <summary>
/// Prints one note through Windows' print dialog (#248; B21, G25, I6), the way docs/research/printing.md recommends:
/// <c>PrintManagerInterop</c> with the window's handle and a <see cref="PrintDocument"/> whose pages are the read view's
/// own rendering on paper (<see cref="MarkdownReadView.Paper"/>), flowed over pages with RichTextBlockOverflow and laid
/// out again whenever the dialog changes the paper or its orientation. Nothing goes through a web view or a file.
/// </summary>
internal sealed class NotePrinter
{
    private readonly Window _window;
    private readonly Panel _root;

    // Pages are laid out here, in the window's tree but never seen: a RichTextBlockOverflow only knows what overflows
    // once it has been measured (the WindowsAppSDK-Samples Printing sample's PrintCanvas).
    private readonly Canvas _layout = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly List<UIElement> _pages = [];
    private PrintDocument? _document;
    private IPrintDocumentSource? _source;
    private Job? _job;

    /// <param name="window">The window the dialog belongs to.</param>
    /// <param name="root">Where the hidden layout canvas goes, once, on the first print.</param>
    public NotePrinter(Window window, Panel root)
    {
        _window = window;
        _root = root;
    }

    /// <summary>Raised on the UI thread when the print task is over, however it ended (printed, cancelled, failed).</summary>
    public event EventHandler<PrintTaskCompletion>? Completed;

    /// <summary>A dialog is up or opening: another print waits for it to close.</summary>
    public bool Busy => _job is not null;

    private sealed record Job(string Title, string Source, NoteMode Mode, IReadOnlyDictionary<string, MarkdownReadView.PrintedPicture> Pictures);

    /// <summary>
    /// Opens Windows' print dialog for a note. False when printing is not available here (Windows 10 before
    /// 19045.2788, microsoft-ui-xaml#4419): <c>PrintManager.IsSupported()</c> is false or the call throws, and nothing
    /// else happens. True when the dialog opened; <see cref="Completed"/> follows when it closes.
    /// </summary>
    public async Task<bool> Print(string title, string source, NoteMode mode)
    {
        if (_job is not null)
        {
            return true;
        }

        try
        {
            if (!PrintManager.IsSupported())
            {
                Trace.WriteLine("Noto.Print unsupported");
                return false;
            }

            // Pictures are decoded before the dialog asks for a page: microsoft-ui-xaml#11095 prints a BitmapImage that
            // is still decoding as a blank.
            _job = new Job(title, source, mode, await Pictures(source, mode));
            nint hwnd = WindowNative.GetWindowHandle(_window);

            // Registered once, on the first print: registering twice throws, and a Noto that never prints pays nothing.
            if (_document is null)
            {
                _document = new PrintDocument();
                _source = _document.DocumentSource;
                _document.Paginate += OnPaginate;
                _document.GetPreviewPage += OnGetPreviewPage;
                _document.AddPages += OnAddPages;
                _root.Children.Add(_layout);
                PrintManagerInterop.GetForWindow(hwnd).PrintTaskRequested += OnPrintTaskRequested;
            }

            bool shown = await PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd);
            Trace.WriteLine($"Noto.Print shown={shown} pictures={_job.Pictures.Count}");

            if (!shown)
            {
                Finish();
            }

            return shown;
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            // Windows 10 before 19045.2788 (KB5023773) throws ArgumentException here (microsoft-ui-xaml#4419).
            Trace.WriteLine($"Noto.Print failed error={ex.GetType().Name} hresult=0x{ex.HResult:X8}");
            Finish();
            return false;
        }
    }

    /// <summary>The note's pictures that Noto keeps and can read, decoded, by their reference. A Markdown note only.</summary>
    private static async Task<Dictionary<string, MarkdownReadView.PrintedPicture>> Pictures(string source, NoteMode mode)
    {
        var pictures = new Dictionary<string, MarkdownReadView.PrintedPicture>(StringComparer.Ordinal);
        if (mode != NoteMode.Markdown || MarkdownReadView.ResolveImage is not { } resolve)
        {
            return pictures;
        }

        MarkdownDocument document = MarkdownDocument.Parse(source);
        foreach (MarkdownBlock block in document.Blocks)
        {
            foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image))
            {
                if (MarkdownReadView.ReferenceOf(document, block, image) is { } reference && !pictures.ContainsKey(reference)
                    && resolve(reference) is { } path && await Decode(path) is { } picture)
                {
                    pictures[reference] = picture;
                }
            }
        }

        return pictures;
    }

    /// <summary>
    /// The picture's pixels, oriented, no larger than 2000 px on a side (sharp at 300 dpi across a page's text width),
    /// in a SoftwareBitmapSource: decoded now, unlike a BitmapImage, which decodes when it is first drawn. Null when the
    /// file cannot be read or is not an image the system decodes.
    /// </summary>
    private static async Task<MarkdownReadView.PrintedPicture?> Decode(string path)
    {
        try
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            double scale = Math.Min(1, 2000.0 / Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
                ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            // Handed to the job, which disposes it when it is over; disposed here only if setting its pixels failed.
            SoftwareBitmapSource? source = new();
            try
            {
                await source.SetBitmapAsync(bitmap);
                var picture = new MarkdownReadView.PrintedPicture(source, bitmap.PixelWidth, bitmap.PixelHeight);
                source = null;
                return picture;
            }
            finally
            {
                source?.Dispose();
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Raised off the UI thread: only the task is made here, from what the job already holds.
    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        if (_job is not { } job || _source is not { } source)
        {
            return;
        }

        PrintTask task = args.Request.CreatePrintTask(job.Title, request => request.SetSource(source));
        task.Completed += (_, e) => _window.DispatcherQueue.TryEnqueueGuarded(() =>
        {
            Trace.WriteLine($"Noto.Print completed completion={e.Completion} pages={_pages.Count}");
            Finish();
            Completed?.Invoke(this, e.Completion);
        });
    }

    /// <summary>The job is over: its pages and pictures are let go, and the next print may start.</summary>
    private void Finish()
    {
        foreach (MarkdownReadView.PrintedPicture picture in _job?.Pictures.Values ?? [])
        {
            picture.Source.Dispose();
        }

        _job = null;
        _pages.Clear();
        _layout.Children.Clear();
    }

    /// <summary>
    /// Lays the note out on pages of the size the dialog chose, again whenever the paper or orientation changes: the
    /// read view on the first page, then a RichTextBlockOverflow per page while text is left over.
    /// </summary>
    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _pages.Clear();
        _layout.Children.Clear();
        if (_job is not { } job)
        {
            _document!.SetPreviewPageCount(0, PreviewPageCountType.Final);
            return;
        }

        PrintPageDescription page = e.PrintTaskOptions.GetPageDescription(0);
        PrintArea area = PrintPage.TextArea(page.PageSize.Width, page.PageSize.Height, page.ImageableRect.X, page.ImageableRect.Y, page.ImageableRect.Width, page.ImageableRect.Height);
        var view = new MarkdownReadView(job.Source, job.Mode, new MarkdownReadView.Paper(job.Pictures, area.Width, PrintPage.PictureMaxHeight(area)));
        FrameworkElement body = view;
        RichTextBlockOverflow? last = null;

        while (true)
        {
            body.Width = area.Width;
            body.Height = area.Height;
            var sheet = new Border
            {
                Width = page.PageSize.Width,
                Height = page.PageSize.Height,
                Padding = new Thickness(area.Left, area.Top, area.Right, area.Bottom),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                RequestedTheme = ElementTheme.Light,
                Child = body,
            };
            _layout.Children.Add(sheet);
            _layout.UpdateLayout();
            _pages.Add(sheet);

            if (!(last?.HasOverflowContent ?? view.Text.HasOverflowContent) || _pages.Count == PrintPage.MaxPages)
            {
                break;
            }

            var next = new RichTextBlockOverflow();
            if (last is null)
            {
                view.Text.OverflowContentTarget = next;
            }
            else
            {
                last.OverflowContentTarget = next;
            }

            body = last = next;
        }

        _document!.SetPreviewPageCount(_pages.Count, PreviewPageCountType.Final);
        Trace.WriteLine($"Noto.Print paginated pages={_pages.Count}");
    }

    private void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e)
    {
        if (e.PageNumber >= 1 && e.PageNumber <= _pages.Count)
        {
            _document!.SetPreviewPage(e.PageNumber, _pages[e.PageNumber - 1]);
        }
    }

    private void OnAddPages(object sender, AddPagesEventArgs e)
    {
        foreach (UIElement page in _pages)
        {
            _document!.AddPage(page);
        }

        _document!.AddPagesComplete();
    }
}
