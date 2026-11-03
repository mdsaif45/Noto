namespace Noto.Core.Editing;

/// <summary>
/// A selection in a note's source (ADR-017 §3): UTF-16 offsets into the LF source. <see cref="Anchor"/> is where it
/// started, <see cref="Active"/> where the caret is; equal, it is a caret.
/// </summary>
public readonly record struct TextSelection(int Anchor, int Active)
{
    public int Start => Math.Min(Anchor, Active);

    public int End => Math.Max(Anchor, Active);

    public int Length => End - Start;

    public bool IsCaret => Anchor == Active;

    public static TextSelection Caret(int at) => new(at, at);

    public static TextSelection Range(int start, int end) => new(start, end);
}

/// <summary>
/// Every change to a note's text, typed or commanded (ADR-017 §3): replace <c>[Start, Start + Length)</c> with
/// <see cref="Replacement"/>, then select <see cref="After"/>.
/// </summary>
public readonly record struct TextEdit(int Start, int Length, string Replacement, TextSelection After)
{
    /// <summary>The text with this edit made.</summary>
    public string ApplyTo(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Concat(text.AsSpan(0, Start), Replacement, text.AsSpan(Start + Length));
    }
}

/// <summary>The edit that turns one text into another: what the user just did in the editor (ADR-017 §3).</summary>
public static class TextDiff
{
    /// <summary>
    /// The smallest single replacement from <paramref name="before"/> to <paramref name="after"/>: the common prefix
    /// and suffix are kept. <see langword="null"/> when they are the same.
    /// </summary>
    public static TextEdit? Between(string before, string after, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        int prefix = before.AsSpan().CommonPrefixLength(after);

        if (prefix == before.Length && prefix == after.Length)
        {
            return null;
        }

        int room = Math.Min(before.Length, after.Length) - prefix;
        int suffix = 0;
        while (suffix < room && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        return new TextEdit(prefix, before.Length - prefix - suffix, after[prefix..(after.Length - suffix)], selection);
    }
}
