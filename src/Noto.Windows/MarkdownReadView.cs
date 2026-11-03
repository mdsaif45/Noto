using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
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

    public static readonly DependencyProperty SourceProperty = Property(nameof(Source), typeof(string));
    public static readonly DependencyProperty MarkupBrushProperty = Property(nameof(MarkupBrush), typeof(Brush));
    public static readonly DependencyProperty LinkBrushProperty = Property(nameof(LinkBrush), typeof(Brush));

    private readonly RichTextBlock _view = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };

    public MarkdownReadView() => Content = _view;

    /// <summary>
    /// Opens a link a card's reader clicked (ADR-019 §2): set once by the window, which checks and opens it. A click on
    /// a card's link has nothing else to do, the card being a read view.
    /// </summary>
    internal static Action<string>? OpenLink { get; set; }

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

        foreach (MarkdownBlock block in document.Blocks)
        {
            var paragraph = new Paragraph();
            Inlines(document, block, paragraph, markup, link, code);
            view.Blocks.Add(paragraph);
        }
    }

    /// <summary>The block's text, cut where its spans start and end, each run styled by the spans over it.</summary>
    private static void Inlines(MarkdownDocument document, MarkdownBlock block, Paragraph paragraph, Brush? markup, Brush? link, FontFamily code)
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

    private static void Piece(MarkdownDocument document, MarkdownBlock block, int start, int end, Paragraph paragraph, Brush? markup, Brush? link, FontFamily code)
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

        foreach (MarkdownSpan span in over)
        {
            switch (span.Kind)
            {
                case MarkdownSpanKind.Heading:
                    run.FontWeight = FontWeights.SemiBold;
                    run.FontSize = (double)Application.Current.Resources[span.Level switch { 1 => "FontSizeHeading", 2 => "FontSizeHeading2", _ => "FontSizeHeading3" }];
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
                case MarkdownSpanKind.Quote:
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
