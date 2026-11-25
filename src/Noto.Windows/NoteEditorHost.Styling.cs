using System.Globalization;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Noto.Core.Markdown;
using Windows.UI;

namespace Noto;

/// <summary>
/// What the editor styles with: the theme's colours and the design system's type, resolved for this theme, and the note
/// text's line height and paragraph spacing (#234, <see cref="NoteSpacing"/>): <paramref name="LineHeight"/> a multiple
/// of each line's text size, 0 for the font's own; <paramref name="SpaceAfter"/> in DIPs.
/// </summary>
internal readonly record struct EditorPalette(
    Color Text,
    Color Markup,
    Color Link,
    Color Quote,
    Color CodeSurface,
    Color Surface,
    Color Mark,
    Color MarkText,
    string BodyFont,
    string CodeFont,
    double BodySize,
    double Heading1Size,
    double Heading2Size,
    double Heading3Size,
    bool ShowMarkup,
    double LineHeight,
    double SpaceAfter,
    bool SpacesHeadings)
{
    public double HeadingSize(int level) => level switch { 1 => Heading1Size, 2 => Heading2Size, _ => Heading3Size };

    /// <summary>
    /// Whether a paragraph has a line height or a space after of its own: Normal for both has neither, and its RTF is
    /// Noto's from before the settings, exactly (#234).
    /// </summary>
    public bool Spaced => LineHeight > 0 || SpaceAfter > 0;
}

/// <summary>
/// Turns Core's spans into RichEdit formats (ADR-017 §4): the whole note as one RTF string when editing starts, and a
/// changed region through batched TOM calls after an edit. No rule is decided here: what is bold is the parse's
/// answer. The text is never changed: RTF carries exactly the source, markup as hidden (\v) text.
/// </summary>
internal static class MarkdownStyling
{
    [Flags]
    private enum Look
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Strike = 4,
        Underline = 8,
        Mono = 16,
        Link = 32,
        Dim = 64,
        Hidden = 128,
        CodeSurface = 256,

        /// <summary>Drawn in the editor's own surface colour: it keeps its width, and the checkbox overlay draws there.</summary>
        Invisible = 512,

        /// <summary>A highlight (F8, #151): the mark's colour behind, its text colour on it.</summary>
        Mark = 1024,

        /// <summary>A quote's text, in its own colour (#269, ADR-023 §7).</summary>
        Quote = 2048,
    }

    /// <summary>
    /// Each look's RTF control word. Colour 2 is markup, 3 a link, 4 the code surface, 5 the editor's surface, 6 a
    /// highlight, 7 its text and 8 a quote (the colour table's order). A highlight comes before code and links, so code inside a
    /// highlight keeps its own surface and a link its colour.
    /// </summary>
    private static readonly (Look Flag, string Word)[] ControlWords =
    [
        (Look.Bold, @"\b"), (Look.Italic, @"\i"), (Look.Strike, @"\strike"), (Look.Underline, @"\ul"), (Look.Mono, @"\f1"),
        (Look.Mark, @"\highlight6\cf7"), (Look.CodeSurface, @"\highlight4"), (Look.Link, @"\cf3"), (Look.Dim, @"\cf2"), (Look.Quote, @"\cf8"), (Look.Hidden, @"\v"), (Look.Invisible, @"\cf5"),
    ];

    /// <summary>Space above a heading that is not the note's first line, in points: it separates sections.</summary>
    private const float HeadingSpaceBefore = 8;

    /// <summary>
    /// A span's look. In a task item the marker and box are invisible but keep their width: the editor draws a real
    /// checkbox over them (NoteEditorHost.TaskMarks). With markup shown, they are the source again.
    /// </summary>
    private static Look Of(MarkdownSpanKind kind, bool showMarkup, bool inTask) => kind switch
    {
        MarkdownSpanKind.ListMarker or MarkdownSpanKind.TaskBox when inTask && !showMarkup => Look.Invisible,
        MarkdownSpanKind.Markup or MarkdownSpanKind.LinkTarget => showMarkup ? Look.Dim : Look.Hidden,
        MarkdownSpanKind.Heading or MarkdownSpanKind.Strong => Look.Bold,
        MarkdownSpanKind.Emphasis => Look.Italic,
        MarkdownSpanKind.Strikethrough => Look.Strike,
        MarkdownSpanKind.Underline => Look.Underline,
        MarkdownSpanKind.Highlight => Look.Mark,
        MarkdownSpanKind.Code or MarkdownSpanKind.CodeBlock => Look.Mono | Look.CodeSurface,
        MarkdownSpanKind.Link => Look.Link | Look.Underline,
        MarkdownSpanKind.Quote => Look.Italic | Look.Quote,
        MarkdownSpanKind.Image => Look.Italic | Look.Dim,
        MarkdownSpanKind.ListMarker or MarkdownSpanKind.TaskBox or MarkdownSpanKind.Separator => Look.Dim,
        MarkdownSpanKind.TaskDone => Look.Strike | Look.Dim,
        _ => Look.None,
    };

    /// <summary>The whole note as RTF: one SetText styles everything (spike: one call, not one per span).</summary>
    public static string Rtf(MarkdownDocument document, EditorPalette palette)
    {
        string text = document.Source;
        var looks = new Look[text.Length];
        var levels = new int[text.Length];

        // Where each heading starts, and its level; where each block's last line starts, the line the space after goes
        // under (#234): a code fence is one block, spaced after its last line as the read view spaces its paragraph.
        var headings = new Dictionary<int, int>();
        var lastLines = new HashSet<int>();

        foreach (MarkdownBlock block in document.Blocks)
        {
            if (block.Kind == MarkdownBlockKind.Heading)
            {
                headings[block.Start] = block.Level;
            }

            lastLines.Add(LastLineStart(text, block));

            foreach (MarkdownSpan span in block.Spans)
            {
                Look look = Of(span.Kind, palette.ShowMarkup, block.Kind == MarkdownBlockKind.TaskItem);
                for (int i = span.Start; i < span.End; i++)
                {
                    looks[i] |= look;
                    if (span.Kind == MarkdownSpanKind.Heading)
                    {
                        levels[i] = span.Level;
                    }
                }
            }
        }

        var rtf = new StringBuilder(text.Length * 2 + 256);
        rtf.Append(@"{\rtf1\ansi\uc1\deff0{\fonttbl{\f0 ").Append(palette.BodyFont).Append(@";}{\f1 ").Append(palette.CodeFont).Append(";}}")
           .Append(@"{\colortbl;").Append(Rgb(palette.Text)).Append(Rgb(palette.Markup)).Append(Rgb(palette.Link)).Append(Rgb(palette.CodeSurface)).Append(Rgb(palette.Surface)).Append(Rgb(palette.Mark)).Append(Rgb(palette.MarkText)).Append(Rgb(palette.Quote)).Append('}')
           .Append(@"\f0\fs").Append(HalfPoints(palette.BodySize)).Append(@"\cf1 ");

        int run = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && looks[i] == looks[run] && levels[i] == levels[run] && text[i] != '\n' && text[i - 1] != '\n')
            {
                continue;
            }

            // A paragraph's spacing is set where its text starts: \pard resets it; \sb, \sa and \sl are in twentieths of
            // a point, \sl negative for exactly that height. An empty line keeps the paragraph before's, unless a line
            // height or space after is set (#234): then it has its own, as Restyle gives it.
            if ((run == 0 || text[run - 1] == '\n') && text.Length > 0 && (run == text.Length || text[run] != '\n' || palette.Spaced))
            {
                bool heading = headings.TryGetValue(run, out int level);
                rtf.Append(@"\pard");
                if (heading && run > 0 && palette.SpacesHeadings)
                {
                    rtf.Append(@"\sb").Append((int)(HeadingSpaceBefore * 20));
                }

                if (palette.LineHeight > 0)
                {
                    rtf.Append(@"\sl-").Append(Twips(palette.LineHeight * (heading ? palette.HeadingSize(level) : palette.BodySize))).Append(@"\slmult0");
                }

                if (palette.SpaceAfter > 0 && lastLines.Contains(run))
                {
                    rtf.Append(@"\sa").Append(Twips(palette.SpaceAfter));
                }

                rtf.Append(' ');
            }

            Emit(rtf, text, run, i, looks[run], levels[run], palette);
            run = i;
        }

        return rtf.Append('}').ToString();
    }

    /// <summary>Restyles <c>[start, end)</c> of the document from the parse: a reset to body text, then each span in it.</summary>
    public static void Restyle(RichEditTextDocument target, MarkdownDocument document, int start, int end, EditorPalette palette)
    {
        if (end <= start)
        {
            return;
        }

        target.BatchDisplayUpdates();

        try
        {
            ITextCharacterFormat body = target.GetRange(start, end).CharacterFormat;
            body.Bold = FormatEffect.Off;
            body.Italic = FormatEffect.Off;
            body.Strikethrough = FormatEffect.Off;
            body.Underline = UnderlineType.None;
            body.Hidden = FormatEffect.Off;
            body.Name = palette.BodyFont;
            body.Size = Points(palette.BodySize);
            body.ForegroundColor = palette.Text;
            body.BackgroundColor = Colors.Transparent;

            foreach (MarkdownBlock block in document.Blocks)
            {
                if (block.End < start || block.Start > end)
                {
                    continue;
                }

                // A heading's space above, as the load gave it; any other block in the region has none. Each line its
                // height, and the block's last line the paragraph spacing below (#234). No block has more below or an
                // indent until an image's or a shortcut's is set again where it is drawn (NoteEditorHost.PlaceImages,
                // PlaceShortcuts).
                bool heading = block.Kind == MarkdownBlockKind.Heading;
                int lastLine = LastLineStart(document.Source, block);
                ITextParagraphFormat paragraph = target.GetRange(block.Start, block.End).ParagraphFormat;
                paragraph.SpaceBefore = heading && block.Start > 0 && palette.SpacesHeadings ? HeadingSpaceBefore : 0;
                paragraph.SpaceAfter = lastLine == block.Start ? Points(palette.SpaceAfter) : 0;
                paragraph.SetIndents(0, 0, 0);
                if (palette.LineHeight > 0)
                {
                    paragraph.SetLineSpacing(LineSpacingRule.Exactly, Points(palette.LineHeight * (heading ? palette.HeadingSize(block.Level) : palette.BodySize)));
                }
                else
                {
                    paragraph.SetLineSpacing(LineSpacingRule.Single, 1);
                }

                if (lastLine != block.Start && palette.SpaceAfter > 0)
                {
                    target.GetRange(lastLine, block.End).ParagraphFormat.SpaceAfter = Points(palette.SpaceAfter);
                }

                foreach (MarkdownSpan span in block.Spans)
                {
                    int from = Math.Max(span.Start, start);
                    int to = Math.Min(span.End, end);

                    if (to > from)
                    {
                        Apply(target.GetRange(from, to).CharacterFormat, Of(span.Kind, palette.ShowMarkup, block.Kind == MarkdownBlockKind.TaskItem), span, palette);
                    }
                }
            }
        }
        finally
        {
            target.ApplyDisplayUpdates();
        }
    }

    private static void Apply(ITextCharacterFormat format, Look look, MarkdownSpan span, EditorPalette palette)
    {
        // Each property set is one formatting call, the editor's unit of cost (ADR-017 §4): only what the span needs.
        if (look.HasFlag(Look.Hidden))
        {
            format.Hidden = FormatEffect.On;
        }

        if (look.HasFlag(Look.Bold))
        {
            format.Bold = FormatEffect.On;
        }

        if (look.HasFlag(Look.Italic))
        {
            format.Italic = FormatEffect.On;
        }

        if (look.HasFlag(Look.Strike))
        {
            format.Strikethrough = FormatEffect.On;
        }

        if (look.HasFlag(Look.Underline))
        {
            format.Underline = UnderlineType.Single;
        }

        if (look.HasFlag(Look.Mono))
        {
            format.Name = palette.CodeFont;
        }

        if (look.HasFlag(Look.Mark) && !look.HasFlag(Look.CodeSurface))
        {
            format.BackgroundColor = palette.Mark;
            format.ForegroundColor = palette.MarkText;
        }

        if (look.HasFlag(Look.CodeSurface))
        {
            format.BackgroundColor = palette.CodeSurface;
        }

        if (look.HasFlag(Look.Link) || look.HasFlag(Look.Dim) || look.HasFlag(Look.Quote))
        {
            format.ForegroundColor = look.HasFlag(Look.Link) ? palette.Link : look.HasFlag(Look.Quote) ? palette.Quote : palette.Markup;
        }

        if (look.HasFlag(Look.Invisible))
        {
            format.ForegroundColor = palette.Surface;
        }

        if (span.Kind == MarkdownSpanKind.Heading)
        {
            format.Size = Points(palette.HeadingSize(span.Level));
        }
    }

    private static void Emit(StringBuilder rtf, string text, int start, int end, Look look, int level, EditorPalette palette)
    {
        if (start == end)
        {
            return;
        }

        if (end - start == 1 && text[start] == '\n')
        {
            rtf.Append(@"\par ");
            return;
        }

        rtf.Append('{');
        int bare = rtf.Length;
        foreach ((Look flag, string word) in ControlWords)
        {
            if (look.HasFlag(flag))
            {
                rtf.Append(word);
            }
        }

        if (level > 0)
        {
            rtf.Append(@"\fs").Append(HalfPoints(palette.HeadingSize(level)));
        }

        // The space ends the last control word; with none written it would be text.
        if (rtf.Length > bare)
        {
            rtf.Append(' ');
        }

        for (int i = start; i < end; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\\' or '{' or '}': rtf.Append('\\').Append(c); break;
                case '\t': rtf.Append(@"\tab "); break;
                case < (char)0x80: rtf.Append(c); break;
                default: rtf.Append(@"\u").Append(((short)c).ToString(CultureInfo.InvariantCulture)).Append('?'); break;
            }
        }

        rtf.Append('}');
    }

    /// <summary>
    /// Where <paramref name="block"/>'s last line starts: its start, but in a code fence, the only block of more than one
    /// line (#234).
    /// </summary>
    private static int LastLineStart(string text, MarkdownBlock block) =>
        block.Length == 0 ? block.Start : Math.Max(block.Start, text.LastIndexOf('\n', block.End - 1, block.Length) + 1);

    /// <summary>DIPs as twentieths of a point, RTF's unit.</summary>
    private static int Twips(double dips) => (int)Math.Round(dips * 15);

    private static string Rgb(Color c) => string.Create(CultureInfo.InvariantCulture, $@"\red{c.R}\green{c.G}\blue{c.B};");

    /// <summary>XAML sizes are device-independent pixels; RichEdit's are points (3/4 of one).</summary>
    private static float Points(double dips) => (float)(dips * 0.75);

    private static string HalfPoints(double dips) => ((int)Math.Round(dips * 1.5)).ToString(CultureInfo.InvariantCulture);
}
