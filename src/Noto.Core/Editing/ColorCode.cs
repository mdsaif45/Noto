using System.Globalization;
using System.Text.RegularExpressions;

namespace Noto.Core.Editing;

/// <summary>
/// A <c>#rrggbb</c> colour code written in a note (F14, #155; parity D15): where it is, and the colour it names.
/// </summary>
/// <param name="Index">Where the <c>#</c> is.</param>
/// <param name="Length">Always 7: the <c>#</c> and six hex digits.</param>
public readonly record struct ColorCode(int Index, int Length, byte R, byte G, byte B)
{
    // Exactly six hex digits after a #, as a word of its own: not part of a longer word or number ("a#abcdef",
    // "#abcdef12"), a character reference ("&#123456") or a path or fragment ("/#abcdef").
    private static readonly Regex Pattern = new(@"(?<![\w&/#])#([0-9A-Fa-f]{6})(?!\w)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Every colour code in <paramref name="text"/>, in order.</summary>
    public static IEnumerable<ColorCode> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (Match match in Pattern.Matches(text))
        {
            string hex = match.Groups[1].Value;
            yield return new ColorCode(
                match.Index,
                match.Length,
                byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The colour as <paramref name="format"/> writes it, for its swatch to copy (#257; parity J17).
    /// </summary>
    /// <param name="format">The Colour copy format setting.</param>
    /// <param name="text">The text this code was found in: As written copies the code from it, exactly as typed.</param>
    public string Format(ColorCopyFormat format, string text) => format switch
    {
        ColorCopyFormat.Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}"),
        ColorCopyFormat.Rgb => string.Create(CultureInfo.InvariantCulture, $"rgb({R}, {G}, {B})"),
        ColorCopyFormat.Hsl => Hsl(),
        _ => text.Substring(Index, Length),
    };

    /// <summary>
    /// <c>hsl(h, s%, l%)</c>, each rounded to a whole number, a half up. Worked from the whole channel values and divided
    /// once, so a value that is exactly a half (a hue of 0.5, a saturation of 12.5%) stays one and rounds up.
    /// </summary>
    private string Hsl()
    {
        int max = Math.Max(R, Math.Max(G, B));
        int min = Math.Min(R, Math.Min(G, B));
        int delta = max - min;
        int sum = max + min;

        // A grey has no hue and no saturation: 0 for both, as CSS writes it.
        double hue = delta == 0 ? 0
            : max == R ? 60.0 * (G - B) / delta
            : max == G ? (60.0 * (B - R) / delta) + 120
            : (60.0 * (R - G) / delta) + 240;
        double saturation = delta == 0 ? 0 : 100.0 * delta / (255 - Math.Abs(sum - 255));
        double lightness = 100.0 * sum / 510;

        // A red a little towards blue has a hue just under 0, which is just under 360; rounded up, 360 is 0 again.
        int h = Whole(hue < 0 ? hue + 360 : hue) % 360;
        return string.Create(CultureInfo.InvariantCulture, $"hsl({h}, {Whole(saturation)}%, {Whole(lightness)}%)");
    }

    private static int Whole(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>How a swatch copies its colour code (#257; parity J17).</summary>
public enum ColorCopyFormat
{
    /// <summary>The code exactly as typed in the note: the default.</summary>
    AsWritten,

    /// <summary><c>#RRGGBB</c>, upper case.</summary>
    Hex,

    /// <summary><c>rgb(r, g, b)</c>.</summary>
    Rgb,

    /// <summary><c>hsl(h, s%, l%)</c>, rounded to whole numbers.</summary>
    Hsl,
}
