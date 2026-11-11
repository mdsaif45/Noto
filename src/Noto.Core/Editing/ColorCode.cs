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
}
