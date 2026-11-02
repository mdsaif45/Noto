using System.Globalization;
using System.Text;

namespace Noto.Spike.HiddenMarkdown;

/// <summary>
/// The same spans as one RTF string, so a whole note is styled by a single SetText call instead of one TOM call per
/// span. Markup is <c>\v</c> (hidden) text: still in the document, still the source.
/// </summary>
internal static class Rtf
{
    [Flags]
    private enum Attr
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Strike = 4,
        Underline = 8,
        Mark = 16,
        Mono = 32,
        Link = 64,
        Dim = 128,
        Hidden = 256,
    }

    private static readonly int[] HeadingHalfPoints = [0, 48, 40, 36, 32, 30];

    public static string FromSpans(string text, List<Span> spans, bool showMarkup)
    {
        var attrs = new Attr[text.Length];
        var size = new int[text.Length];

        foreach (Span s in spans)
        {
            Attr a = s.Kind switch
            {
                SpanKind.Markup => showMarkup ? Attr.Dim : Attr.Hidden,
                SpanKind.Strong => Attr.Bold,
                SpanKind.Emphasis => Attr.Italic,
                SpanKind.Strike => Attr.Strike,
                SpanKind.Underline => Attr.Underline,
                SpanKind.Mark => Attr.Mark,
                SpanKind.Code or SpanKind.CodeBlock => Attr.Mono,
                SpanKind.Link => Attr.Link | Attr.Underline,
                SpanKind.Heading => Attr.Bold,
                SpanKind.Quote => Attr.Italic | Attr.Dim,
                SpanKind.ListMarker or SpanKind.TaskMarker or SpanKind.Rule => Attr.Dim,
                SpanKind.TaskDone => Attr.Strike | Attr.Dim,
                _ => Attr.None,
            };

            for (int i = s.Start; i < s.End; i++)
            {
                attrs[i] |= a;
                if (s.Kind == SpanKind.Heading)
                {
                    size[i] = HeadingHalfPoints[s.Level];
                }
            }
        }

        var rtf = new StringBuilder(text.Length * 2);
        rtf.Append(@"{\rtf1\ansi\uc1\deff0{\fonttbl{\f0 Segoe UI Variable Text;}{\f1 Consolas;}}")
           .Append(@"{\colortbl;\red27\green27\blue27;\red128\green128\blue128;\red0\green95\blue184;\red255\green241\blue156;}")
           .Append(@"\f0\fs28\cf1 ");

        int run = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && attrs[i] == attrs[run] && size[i] == size[run] && text[i] != '\n' && text[i - 1] != '\n')
            {
                continue;
            }

            Emit(rtf, text, run, i, attrs[run], size[run]);
            run = i;
        }

        return rtf.Append('}').ToString();
    }

    private static void Emit(StringBuilder rtf, string text, int start, int end, Attr a, int halfPoints)
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
        if (a.HasFlag(Attr.Bold)) rtf.Append(@"\b");
        if (a.HasFlag(Attr.Italic)) rtf.Append(@"\i");
        if (a.HasFlag(Attr.Strike)) rtf.Append(@"\strike");
        if (a.HasFlag(Attr.Underline)) rtf.Append(@"\ul");
        if (a.HasFlag(Attr.Mark)) rtf.Append(@"\highlight4");
        if (a.HasFlag(Attr.Mono)) rtf.Append(@"\f1");
        if (a.HasFlag(Attr.Link)) rtf.Append(@"\cf3");
        if (a.HasFlag(Attr.Dim)) rtf.Append(@"\cf2");
        if (a.HasFlag(Attr.Hidden)) rtf.Append(@"\v");
        if (halfPoints > 0) rtf.Append(@"\fs").Append(halfPoints.ToString(CultureInfo.InvariantCulture));
        if (rtf.Length > bare)
        {
            // The space ends the last control word; with none written it would be text.
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
}
