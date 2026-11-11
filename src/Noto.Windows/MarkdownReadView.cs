using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Noto.Core.Images;
using Noto.Core.Links;
using Noto.Core.Markdown;
using Windows.UI.Text;

namespace Noto;

/// <summary>
/// A card's read view (ADR-016 §3): the note rendered from the same Core parse the editor styles with, markup left
/// out. The brushes are theme resources, so a theme switch renders it again.
/// </summary>
internal sealed partial class MarkdownReadView : UserControl
{
    /// <summary>
    /// Above this length the read view shows the source as plain text, as cards did before P2: a RichTextBlock of a
    /// very long note costs hundreds of milliseconds to lay out (spike), and nothing is lost that the editor does not show.
    /// </summary>
    private const int RenderedLimit = 20_000;

    /// <summary>The tallest a card draws an image: a card is a glance at the note, the editor shows it larger.</summary>
    private const double ImageMaxHeight = 160;

    /// <summary>A card draws this many of a note's images; the rest stay their captions.</summary>
    private const int ImageLimit = 4;

    public static readonly DependencyProperty SourceProperty = Property(nameof(Source), typeof(string));
    public static readonly DependencyProperty MarkupBrushProperty = Property(nameof(MarkupBrush), typeof(Brush));
    public static readonly DependencyProperty LinkBrushProperty = Property(nameof(LinkBrush), typeof(Brush));

    private readonly RichTextBlock _view = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };

    public MarkdownReadView()
    {
        Content = _view;

        // The note text's family and size come from the card (F13, #154); headings scale with the size.
        _ = RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => Render());
        _ = RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => Render());
    }

    /// <summary>
    /// A heading's size: the design system's heading step over its body size, at this view's size, so a larger note text
    /// keeps its headings a step above it (F13, #154).
    /// </summary>
    private double HeadingSize(int level)
    {
        var resources = Application.Current.Resources;
        double body = (double)resources["FontSizeBody"];
        double heading = (double)resources[level switch { 1 => "FontSizeHeading", 2 => "FontSizeHeading2", _ => "FontSizeHeading3" }];
        return Math.Round(FontSize * heading / body);
    }

    /// <summary>
    /// Opens a link a card's reader clicked (ADR-019 §2): set once by the window, which checks and opens it. A click on
    /// a card's link has nothing else to do, the card being a read view.
    /// </summary>
    internal static Action<string>? OpenLink { get; set; }

    /// <summary>The file of an image's reference when Noto keeps it and it is there (ADR-019 §4); set once by the window.</summary>
    internal static Func<string?, string?>? ResolveImage { get; set; }

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Brush? MarkupBrush
    {
        get => (Brush?)GetValue(MarkupBrushProperty);
        set => SetValue(MarkupBrushProperty, value);
    }

    public Brush? LinkBrush
    {
        get => (Brush?)GetValue(LinkBrushProperty);
        set => SetValue(LinkBrushProperty, value);
    }

    private static DependencyProperty Property(string name, Type type) =>
        DependencyProperty.Register(name, type, typeof(MarkdownReadView), new PropertyMetadata(null, (d, _) => ((MarkdownReadView)d).Render()));

    private void Render()
    {
        RichTextBlock view = _view;
        view.Blocks.Clear();
        string source = Source ?? string.Empty;

        if (source.Length > RenderedLimit)
        {
            var plain = new Paragraph();
            plain.Inlines.Add(new Run { Text = source });
            view.Blocks.Add(plain);
            return;
        }

        MarkdownDocument document = MarkdownDocument.Parse(source);

        // In high contrast the row's own text colour must win: on a selected row that is HighlightText, and a
        // secondary or link colour there measured 1.37:1. So the read view sets no colour of its own; the brushes
        // change with the theme, which renders it again when high contrast turns on or off.
        bool highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        Brush? markup = highContrast ? null : MarkupBrush;
        Brush? link = highContrast ? null : LinkBrush;
        var code = (FontFamily)Application.Current.Resources["FontFamilyCode"];

        double room = ActualWidth > 0 ? ActualWidth : double.PositiveInfinity;
        int images = 0;

        foreach (MarkdownBlock block in document.Blocks)
        {
            var paragraph = new Paragraph();
            Inlines(document, block, paragraph, markup, link, code);

            // Each image below its caption, the caption being what a reader of the screen hears.
            foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image))
            {
                if (ResolveImage is null || ++images > ImageLimit)
                {
                    break;
                }

                var holder = new Border();
                paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new InlineUIContainer { Child = holder });
                _ = Picture(holder, ReferenceOf(document, block, image), room, markup);
            }

            view.Blocks.Add(paragraph);
        }
    }

    /// <summary>The image's source as written, if it has one.</summary>
    private static string? ReferenceOf(MarkdownDocument document, MarkdownBlock block, MarkdownSpan image) =>
        block.Spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start > image.Start && s.End < image.End)
            .Select(s => document.Source.Substring(s.Start, s.Length))
            .LastOrDefault();

    /// <summary>
    /// Draws the image in <paramref name="holder"/> once its size is read: no wider than the card or taller than
    /// <see cref="ImageMaxHeight"/>, never enlarged; a one-line notice when its file is not there or cannot be read.
    /// </summary>
    private static async Task Picture(Border holder, string? reference, double room, Brush? markup)
    {
        string? path = ResolveImage?.Invoke(reference);
        if (path is not null && await ImageFiles.SizeOf(path) is { } size)
        {
            (double width, double height) = ImageSize.Fit(size.Width, size.Height, room, ImageMaxHeight);
            var bitmap = ImageFiles.Bitmap(path, width);
            double gap = (double)Application.Current.Resources["SpaceXS"];
            // A card may be rendered before it is laid out, its width unknown: the picture then only shrinks to the
            // width it is given, its shape kept (measured: drawn at its height's fit, it overflowed the card's padding).
            Border picture = ImageFiles.Picture(bitmap, width, height);
            var image = new Viewbox { Child = picture, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(0, gap, 0, gap) };
            AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
            bitmap.ImageFailed += (_, _) => holder.Child = Notice("This image can't be shown", markup);
            holder.Child = image;
            return;
        }

        holder.Child = Notice(path is null ? "Image not found" : "This image can't be shown", markup);
    }

    private static TextBlock Notice(string text, Brush? markup)
    {
        var notice = new TextBlock { Text = text, FontStyle = FontStyle.Italic, FontSize = (double)Application.Current.Resources["FontSizeCaption"] };
        if (markup is not null)
        {
            notice.Foreground = markup;
        }

        return notice;
    }

    /// <summary>The block's text, cut where its spans start and end, each run styled by the spans over it.</summary>
    private void Inlines(MarkdownDocument document, MarkdownBlock block, Paragraph paragraph, Brush? markup, Brush? link, FontFamily code)
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
                Piece(document, block, previous, cut, paragraph, markup, link, code);
            }

            previous = cut;
        }
    }

    private void Piece(MarkdownDocument document, MarkdownBlock block, int start, int end, Paragraph paragraph, Brush? markup, Brush? link, FontFamily code)
    {
        string source = document.Source;
        var over = block.Spans.Where(s => s.Start <= start && end <= s.End).ToList();

        if (over.Any(s => s.Kind is MarkdownSpanKind.Markup or MarkdownSpanKind.LinkTarget))
        {
            return;
        }

        string text = source[start..end];

        // A read view may draw what the source means: a bullet, a box. The editor shows the source itself.
        if (over.Any(s => s.Kind == MarkdownSpanKind.TaskBox))
        {
            // U+FE0E asks for the text glyph: without it ☑ is drawn as a colour emoji, which keeps its own palette in
            // high contrast and does not match ☐ (measured in the Light and Dark screenshots).
            text = new string([(char)(block.IsChecked ? 0x2611 : 0x2610), (char)0xFE0E]);
        }
        else if (over.Any(s => s.Kind == MarkdownSpanKind.ListMarker) && block.Kind is MarkdownBlockKind.BulletItem or MarkdownBlockKind.TaskItem)
        {
            text = block.Kind == MarkdownBlockKind.TaskItem ? string.Empty : "•  ";
        }

        if (text.Length == 0)
        {
            return;
        }

        var run = new Run { Text = text };
        if (over.Any(s => s.Kind == MarkdownSpanKind.TaskBox))
        {
            run.FontFamily = (FontFamily)Application.Current.Resources["FontFamilySymbol"];
        }

        foreach (MarkdownSpan span in over)
        {
            switch (span.Kind)
            {
                case MarkdownSpanKind.Heading:
                    run.FontWeight = FontWeights.SemiBold;
                    run.FontSize = HeadingSize(span.Level);
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
                case MarkdownSpanKind.Underline:
                    run.TextDecorations |= TextDecorations.Underline;
                    break;
                case MarkdownSpanKind.Code or MarkdownSpanKind.CodeBlock:
                    run.FontFamily = code;
                    break;
                case MarkdownSpanKind.Link:
                    Tint(run, link);
                    break;
                case MarkdownSpanKind.Quote or MarkdownSpanKind.Image:
                    run.FontStyle = FontStyle.Italic;
                    Tint(run, markup);
                    break;
                case MarkdownSpanKind.TaskDone:
                    run.TextDecorations |= TextDecorations.Strikethrough;
                    Tint(run, markup);
                    break;
                case MarkdownSpanKind.ListMarker or MarkdownSpanKind.TaskBox or MarkdownSpanKind.Separator:
                    Tint(run, markup);
                    break;
            }
        }

        // A link opens on a click: a Hyperlink, its own underline, never a tab stop (the stack's keys stay the stack's;
        // the keyboard opens a link from the editor, with Ctrl+Enter).
        if (over.Any(s => s.Kind == MarkdownSpanKind.Link) && LinkTarget.At(document, start) is { } destination)
        {
            var hyperlink = new Hyperlink { IsTabStop = false, UnderlineStyle = UnderlineStyle.Single };
            if (link is not null)
            {
                hyperlink.Foreground = link;
            }

            hyperlink.Inlines.Add(run);
            hyperlink.Click += (_, _) => OpenLink?.Invoke(destination);
            paragraph.Inlines.Add(hyperlink);
            return;
        }

        paragraph.Inlines.Add(run);
    }

    /// <summary>Colours the run, or leaves it the row's colour when there is no brush (high contrast).</summary>
    private static void Tint(Run run, Brush? brush)
    {
        if (brush is not null)
        {
            run.Foreground = brush;
        }
    }
}
