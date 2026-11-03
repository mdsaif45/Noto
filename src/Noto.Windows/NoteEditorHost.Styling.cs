using System.Globalization;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Noto.Core.Markdown;
using Windows.UI;

namespace Noto;

/// <summary>What the editor styles with: the theme's colours and the design system's type, resolved for this theme.</summary>
internal readonly record struct EditorPalette(
    Color Text,
    Color Markup,
    Color Link,
    Color CodeSurface,
    string BodyFont,
    string CodeFont,
    double BodySize,
    double Heading1Size,
    double Heading2Size,
    double Heading3Size,
    bool ShowMarkup)
{
    public double HeadingSize(int level) => level switch { 1 => Heading1Size, 2 => Heading2Size, _ => Heading3Size };
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
    }

    /// <summary>Each look's RTF control word. Colour 2 is markup, 3 a link, 4 the code surface (the colour table's order).</summary>
    private static readonly (Look Flag, string Word)[] ControlWords =
    [
        (Look.Bold, @"\b"), (Look.Italic, @"\i"), (Look.Strike, @"\strike"), (Look.Underline, @"\ul"), (Look.Mono, @"\f1"),
        (Look.CodeSurface, @"\highlight4"), (Look.Link, @"\cf3"), (Look.Dim, @"\cf2"), (Look.Hidden, @"\v"),
    ];

    private static Look Of(MarkdownSpanKind kind, bool showMarkup) => kind switch
    {
        MarkdownSpanKind.Markup or MarkdownSpanKind.LinkTarget => showMarkup ? Look.Dim : Look.Hidden,
        MarkdownSpanKind.Heading or MarkdownSpanKind.Strong => Look.Bold,
        MarkdownSpanKind.Emphasis => Look.Italic,
        MarkdownSpanKind.Strikethrough => Look.Strike,
        MarkdownSpanKind.Underline => Look.Underline,
        MarkdownSpanKind.Code or MarkdownSpanKind.CodeBlock => Look.Mono | Look.CodeSurface,
        MarkdownSpanKind.Link => Look.Link | Look.Underline,
        MarkdownSpanKind.Quote => Look.Italic | Look.Dim,
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

        foreach (MarkdownSpan span in document.Spans)
        {
            Look look = Of(span.Kind, palette.ShowMarkup);
            for (int i = span.Start; i < span.End; i++)
            {
                looks[i] |= look;
                if (span.Kind == MarkdownSpanKind.Heading)
                {
                    levels[i] = span.Level;
                }
            }
        }

        var rtf = new StringBuilder(text.Length * 2 + 256);
        rtf.Append(@"{\rtf1\ansi\uc1\deff0{\fonttbl{\f0 ").Append(palette.BodyFont).Append(@";}{\f1 ").Append(palette.CodeFont).Append(";}}")
           .Append(@"{\colortbl;").Append(Rgb(palette.Text)).Append(Rgb(palette.Markup)).Append(Rgb(palette.Link)).Append(Rgb(palette.CodeSurface)).Append('}')
           .Append(@"\f0\fs").Append(HalfPoints(palette.BodySize)).Append(@"\cf1 ");

        int run = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && looks[i] == looks[run] && levels[i] == levels[run] && text[i] != '\n' && text[i - 1] != '\n')
            {
                continue;
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

                foreach (MarkdownSpan span in block.Spans)
                {
                    int from = Math.Max(span.Start, start);
                    int to = Math.Min(span.End, end);

                    if (to > from)
                    {
                        Apply(target.GetRange(from, to).CharacterFormat, Of(span.Kind, palette.ShowMarkup), span, palette);
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

        if (look.HasFlag(Look.CodeSurface))
        {
            format.BackgroundColor = palette.CodeSurface;
        }

        if (look.HasFlag(Look.Link) || look.HasFlag(Look.Dim))
        {
            format.ForegroundColor = look.HasFlag(Look.Link) ? palette.Link : palette.Markup;
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

    private static string Rgb(Color c) => string.Create(CultureInfo.InvariantCulture, $@"\red{c.R}\green{c.G}\blue{c.B};");

    /// <summary>XAML sizes are device-independent pixels; RichEdit's are points (3/4 of one).</summary>
    private static float Points(double dips) => (float)(dips * 0.75);

    private static string HalfPoints(double dips) => ((int)Math.Round(dips * 1.5)).ToString(CultureInfo.InvariantCulture);
}
