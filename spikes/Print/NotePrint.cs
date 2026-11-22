using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Noto.Core.Images;
using Noto.Core.Markdown;
using Noto.Core.Notes;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Text;

namespace Noto.Spike.Print;

/// <summary>
/// A note made printable (#246): parsed with Noto.Core's <see cref="MarkdownDocument"/> and rendered into a
/// RichTextBlock the way the read view (src/Noto.Windows/MarkdownReadView.cs) renders a card, in ink on paper: black
/// text, no theme brushes, every picture drawn as wide as the page allows. A production print path would share the
/// read view's renderer rather than copy it (docs/research/printing.md, plan step 1).
/// </summary>
internal sealed class NotePrint
{
    private const double Body = 14;
    private static readonly double[] Heading = [0, 20, 17, 15, 15, 15, 15]; // Tokens.xaml FontSizeHeading, 2, 3
    private static readonly FontFamily Code = new("Cascadia Mono, Consolas");
    private static readonly FontFamily Symbol = new("Segoe UI Symbol");
    private static readonly SolidColorBrush Ink = new(Microsoft.UI.Colors.Black);
    private static readonly SolidColorBrush Muted = new(Windows.UI.Color.FromArgb(255, 0x5F, 0x5F, 0x5F));
    private static readonly SolidColorBrush LinkInk = new(Windows.UI.Color.FromArgb(255, 0x00, 0x4E, 0x8C));

    private readonly MarkdownDocument _document;
    private readonly Dictionary<string, Picture> _pictures;

    private NotePrint(MarkdownDocument document, Dictionary<string, Picture> pictures, string title)
    {
        _document = document;
        _pictures = pictures;
        Title = title;
    }

    /// <summary>The note's title, as Noto names it: what the print dialog shows and a PDF is named by.</summary>
    public string Title { get; }

    private sealed record Picture(SoftwareBitmapSource Source, int Width, int Height);

    /// <summary>Reads the note and decodes every picture it refers to, so no page waits on a decode.</summary>
    public static async Task<NotePrint> LoadAsync(string path)
    {
        string source = await File.ReadAllTextAsync(path);
        MarkdownDocument document = MarkdownDocument.Parse(source);
        string folder = Path.GetDirectoryName(path)!;
        var pictures = new Dictionary<string, Picture>(StringComparer.Ordinal);

        foreach (MarkdownBlock block in document.Blocks)
        {
            foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image))
            {
                if (ReferenceOf(document, block, image) is { } reference && !pictures.ContainsKey(reference)
                    && ImageReference.TryFileName(reference, out string name)
                    && await DecodeAsync(Path.Join(folder, ImageReference.Folder, name)) is { } picture)
                {
                    pictures[reference] = picture;
                }
            }
        }

        return new NotePrint(document, pictures, NoteTitle.From(source, NoteMode.Markdown));
    }

    /// <summary>
    /// The picture's pixels, oriented, no wider than 2000 px (sharp at 300 dpi across a page's text width), in a
    /// SoftwareBitmapSource: decoded now, unlike a BitmapImage, which decodes when it is first drawn.
    /// </summary>
    private static async Task<Picture?> DecodeAsync(string path)
    {
        try
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            double scale = Math.Min(1, 2000.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
                ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            return new Picture(source, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// A new RichTextBlock of the note (an element has one parent, and the dialog paginates again on every change of
    /// paper). Pictures fit <paramref name="width"/> by two thirds of <paramref name="height"/>, never enlarged, so one
    /// never outgrows a page.
    /// </summary>
    public RichTextBlock Render(double width, double height)
    {
        var view = new RichTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = Body, Foreground = Ink, IsTextSelectionEnabled = false };
        foreach (MarkdownBlock block in _document.Blocks)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, block.Kind == MarkdownBlockKind.Heading ? 4 : 2) };
            Inlines(block, paragraph);

            foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image))
            {
                paragraph.Inlines.Add(new LineBreak());
                if (ReferenceOf(_document, block, image) is { } reference && _pictures.TryGetValue(reference, out Picture? picture))
                {
                    (double w, double h) = ImageSize.Fit(picture.Width, picture.Height, width, height * 2 / 3);
                    paragraph.Inlines.Add(new InlineUIContainer { Child = new Image { Source = picture.Source, Width = w, Height = h, Stretch = Stretch.Uniform } });
                }
                else
                {
                    paragraph.Inlines.Add(new Run { Text = "Image not found", FontStyle = FontStyle.Italic, Foreground = Muted });
                }
            }

            view.Blocks.Add(paragraph);
        }

        return view;
    }

    private static string? ReferenceOf(MarkdownDocument document, MarkdownBlock block, MarkdownSpan image) =>
        block.Spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start > image.Start && s.End < image.End)
            .Select(s => document.Source.Substring(s.Start, s.Length))
            .LastOrDefault();

    /// <summary>The block's text cut where its spans start and end, each piece styled by the spans over it (as the read view).</summary>
    private void Inlines(MarkdownBlock block, Paragraph paragraph)
    {
        var cuts = new SortedSet<int> { block.Start, block.End };
        foreach (MarkdownSpan span in block.Spans)
        {
            cuts.Add(span.Start);
            cuts.Add(span.End);
        }

        int previous = -1;
        foreach (int cut in cuts)
        {
            if (previous >= 0 && cut > previous)
            {
                Piece(block, previous, cut, paragraph);
            }

            previous = cut;
        }
    }

    private void Piece(MarkdownBlock block, int start, int end, Paragraph paragraph)
    {
        var over = block.Spans.Where(s => s.Start <= start && end <= s.End).ToList();
        if (over.Any(s => s.Kind is MarkdownSpanKind.Markup or MarkdownSpanKind.LinkTarget))
        {
            return;
        }

        string text = _document.Source[start..end];
        var run = new Run();
        if (over.Any(s => s.Kind == MarkdownSpanKind.TaskBox))
        {
            text = new string([(char)(block.IsChecked ? 0x2611 : 0x2610), (char)0xFE0E]);
            run.FontFamily = Symbol;
        }
        else if (over.Any(s => s.Kind == MarkdownSpanKind.ListMarker) && block.Kind is MarkdownBlockKind.BulletItem or MarkdownBlockKind.TaskItem)
        {
            text = block.Kind == MarkdownBlockKind.TaskItem ? string.Empty : "•  ";
        }

        if (text.Length == 0)
        {
            return;
        }

        run.Text = text;
        foreach (MarkdownSpan span in over)
        {
            switch (span.Kind)
            {
                case MarkdownSpanKind.Heading:
                    run.FontWeight = FontWeights.SemiBold;
                    run.FontSize = Heading[Math.Clamp(span.Level, 1, 6)];
                    break;
                case MarkdownSpanKind.Strong:
                    run.FontWeight = FontWeights.Bold;
                    break;
                case MarkdownSpanKind.Emphasis:
                    run.FontStyle = FontStyle.Italic;
                    break;
                case MarkdownSpanKind.Strikethrough:
                    run.TextDecorations |= TextDecorations.Strikethrough;
                    break;
                case MarkdownSpanKind.Underline or MarkdownSpanKind.Highlight:
                    // Paper has no highlighter brush here: a highlight prints underlined (a TextHighlighter is untested in print).
                    run.TextDecorations |= TextDecorations.Underline;
                    break;
                case MarkdownSpanKind.Code or MarkdownSpanKind.CodeBlock:
                    run.FontFamily = Code;
                    break;
                case MarkdownSpanKind.Link:
                    run.Foreground = LinkInk;
                    run.TextDecorations |= TextDecorations.Underline;
                    break;
                case MarkdownSpanKind.Quote or MarkdownSpanKind.Image:
                    run.FontStyle = FontStyle.Italic;
                    run.Foreground = Muted;
                    break;
                case MarkdownSpanKind.TaskDone:
                    run.TextDecorations |= TextDecorations.Strikethrough;
                    run.Foreground = Muted;
                    break;
                case MarkdownSpanKind.ListMarker or MarkdownSpanKind.Separator:
                    run.Foreground = Muted;
                    break;
            }
        }

        paragraph.Inlines.Add(run);
    }

    /// <summary>
    /// Writes a sample note to %TEMP%\noto-print-spike: every kind of block, a picture on page 1 (where #11095 blanks
    /// them) and enough text for a second page with another heading. Returns the note's path.
    /// </summary>
    public static async Task<string> WriteSampleAsync()
    {
        string folder = Path.Join(Path.GetTempPath(), "noto-print-spike");
        string reference = ImageReference.New("png");
        Directory.CreateDirectory(Path.Join(folder, ImageReference.Folder));
        await WriteGradientAsync(Path.Join(folder, reference));

        var lines = new List<string>
        {
            "# Print spike note",
            "",
            "A paragraph with **bold**, *italic*, ~~struck~~, ==underlined== and `inline code`, and a [link](https://example.com).",
            "",
            "## Lists",
            "- a bullet",
            "- another bullet",
            "1. first",
            "2. second",
            "- [ ] a task to do",
            "- [x] a task done",
            "",
            "> A quote, as the read view draws it.",
            "",
            "```",
            "dotnet build Noto.sln -c Release",
            "```",
            "",
            "---",
            "",
            $"![A generated gradient]({reference})",
            "",
            "### Filler for page two",
        };
        for (int i = 1; i <= 60; i++)
        {
            lines.Add($"Line {i}: the quick brown fox jumps over the lazy dog, to fill the page and flow onto the next.");
        }

        lines.Add("## The last heading, on page two");
        lines.Add("- [ ] a task on page two");

        string path = Path.Join(folder, "Print spike note.md");
        await File.WriteAllTextAsync(path, string.Join('\n', lines));
        return path;
    }

    private static async Task WriteGradientAsync(string path)
    {
        const int Width = 480, Height = 270;
        byte[] pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = ((y * Width) + x) * 4;
                pixels[i] = (byte)(255 * x / Width);        // B
                pixels[i + 1] = (byte)(255 * y / Height);   // G
                pixels[i + 2] = 0x60;                       // R
                pixels[i + 3] = 0xFF;                       // A
            }
        }

        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Width, Height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        byte[] png = new byte[stream.Size];
        await stream.ReadAsync(png.AsBuffer(), (uint)png.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, png);
    }
}
