namespace Noto.Core.Themes;

/// <summary>
/// A colour in a theme (ADR-023 §1, #269): sRGB with alpha, read from exactly <c>#rgb</c>, <c>#rrggbb</c> or
/// <c>#rrggbbaa</c>, alpha last as in CSS. It also holds the WCAG 2.x arithmetic every theme, and Noto's own colours, are
/// measured with (§4): one copy, used by the loader and by the design system's guard.
/// </summary>
public readonly record struct ThemeColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Whether it is fully opaque.</summary>
    public bool IsOpaque => A == 255;

    /// <summary>The same colour, opaque.</summary>
    public ThemeColor Opaque => this with { A = 255 };

    /// <summary>
    /// Reads <paramref name="text"/> as <c>#rgb</c> (each digit doubled), <c>#rrggbb</c> or <c>#rrggbbaa</c>, hex digits in
    /// either case. Anything else (a named colour, <c>rgb()</c>, a markup extension) is not a colour.
    /// </summary>
    public static bool TryParse(string? text, out ThemeColor colour)
    {
        colour = default;
        if (text is not { Length: 4 or 7 or 9 } || text[0] != '#')
        {
            return false;
        }

        ReadOnlySpan<char> hex = text.AsSpan(1);
        foreach (char c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        colour = hex.Length == 3
            ? new((byte)(Digit(hex[0]) * 17), (byte)(Digit(hex[1]) * 17), (byte)(Digit(hex[2]) * 17))
            : new(Pair(hex, 0), Pair(hex, 2), Pair(hex, 4), hex.Length == 8 ? Pair(hex, 6) : (byte)255);
        return true;

        static int Digit(char c) => Convert.ToInt32(c.ToString(), 16);
        static byte Pair(ReadOnlySpan<char> hex, int at) => (byte)((Digit(hex[at]) * 16) + Digit(hex[at + 1]));
    }

    /// <summary>This colour laid over <paramref name="back"/> (taken as opaque): the opaque colour that is seen.</summary>
    public ThemeColor Over(ThemeColor back)
    {
        double a = A / 255.0;
        return new(Blend(R, back.R, a), Blend(G, back.G, a), Blend(B, back.B, a));
    }

    /// <summary>This colour mixed <paramref name="amount"/> (0 to 1) of the way toward <paramref name="other"/>, opaque.</summary>
    public ThemeColor Toward(ThemeColor other, double amount) =>
        new(Blend(other.R, R, amount), Blend(other.G, G, amount), Blend(other.B, B, amount));

    /// <summary>WCAG 2.x relative luminance of the colour's RGB.</summary>
    public double Luminance => Lum(R / 255.0, G / 255.0, B / 255.0);

    /// <summary>
    /// The WCAG 2.x contrast ratio of <paramref name="fore"/>, composited over the opaque <paramref name="back"/>; the
    /// composite is not rounded to bytes, so a colour tuned near a threshold measures as it always has.
    /// </summary>
    public static double Contrast(ThemeColor fore, ThemeColor back)
    {
        double a = fore.A / 255.0;
        double l1 = Lum(Mix(fore.R, back.R), Mix(fore.G, back.G), Mix(fore.B, back.B));
        double l2 = back.Luminance;
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

        double Mix(byte f, byte b) => ((a * f) + ((1 - a) * b)) / 255.0;
    }

    public override string ToString() => IsOpaque ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";

    private static byte Blend(byte f, byte b, double a) => (byte)Math.Round((a * f) + ((1 - a) * b));

    private static double Lum(double r, double g, double b) => (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
