using System.Globalization;

namespace Noto.Core.Themes;

/// <summary>
/// A theme's colours for one mode, resolved over Noto's own, and the readability rules every theme is held to (ADR-023
/// §2, §4, §5; #269). Pure: the caller gives Noto's own colours, read from the design system, so the same rules measure a
/// theme at install, a theme at load, and Noto's own colours in the design system's guard.
/// </summary>
/// <remarks>
/// A palette is keyed by <see cref="ThemeFile.ColourKeys"/> plus the derived states (<see cref="Hover"/>,
/// <see cref="Pressed"/>, <see cref="Selected"/>, <see cref="CardHover"/>) and the fixed focus ring's two tones
/// (<see cref="Focus"/>, <see cref="FocusInner"/>), which no theme sets.
/// </remarks>
public static class ThemePalette
{
    /// <summary>A row under the pointer: the background 4 % of the way to the text.</summary>
    public const string Hover = "hover";

    /// <summary>A pressed row: the background 10 % of the way to the text.</summary>
    public const string Pressed = "pressed";

    /// <summary>The selected row: the background 8 % of the way to the text.</summary>
    public const string Selected = "selected";

    /// <summary>A card under the pointer: the card 2.5 % of the way to the text.</summary>
    public const string CardHover = "cardHover";

    /// <summary>The focus ring's outer tone: fixed, never a theme's.</summary>
    public const string Focus = "focus";

    /// <summary>The focus ring's inner tone: fixed, never a theme's.</summary>
    public const string FocusInner = "focusInner";

    /// <summary>Every key a palette of Noto's own must hold.</summary>
    public static IReadOnlyList<string> Keys { get; } = [.. ThemeFile.ColourKeys, Hover, Pressed, Selected, CardHover, Focus, FocusInner];

    private static readonly Pair[] Pairs = [.. Table()];

    // §5: drawn only inside a note, so laid over the card into one opaque colour the editor's RTF can use.
    private static readonly string[] Flattened = ["code", "highlight"];

    /// <summary>
    /// The palette a mode is drawn in: <paramref name="own"/> (Noto's own, every <see cref="Keys"/>) with the theme's
    /// <paramref name="set"/> over it. <c>code</c> and <c>highlight</c> are flattened over the card (§5); the states are
    /// derived from the background, card and text whenever the theme sets one of them (§2), so a partial theme is coherent.
    /// </summary>
    public static Dictionary<string, ThemeColor> Resolve(IReadOnlyDictionary<string, ThemeColor> own, IReadOnlyDictionary<string, ThemeColor> set)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(set);

        var palette = new Dictionary<string, ThemeColor>(own);
        foreach ((string key, ThemeColor colour) in set)
        {
            palette[key] = colour;
        }

        foreach (string flattened in Flattened.Where(set.ContainsKey))
        {
            palette[flattened] = set[flattened].Over(palette["card"]);
        }

        if (set.ContainsKey("background") || set.ContainsKey("card") || set.ContainsKey("text"))
        {
            ThemeColor text = palette["text"];
            palette[Hover] = palette["background"].Toward(text, 0.04);
            palette[Selected] = palette["background"].Toward(text, 0.08);
            palette[Pressed] = palette["background"].Toward(text, 0.10);
            palette[CardHover] = palette["card"].Toward(text, 0.025);
        }

        return palette;
    }

    /// <summary>
    /// Every pair of <paramref name="palette"/> below its minimum in <paramref name="mode"/> (§4), in the table's order.
    /// The accent is measured only when <paramref name="accent"/>: otherwise it is the user's Windows accent, which is not
    /// a theme's, nor Noto's, to measure.
    /// </summary>
    public static IReadOnlyList<ThemeFailure> Measure(IReadOnlyDictionary<string, ThemeColor> palette, ThemeMode mode, bool accent)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var failures = new List<ThemeFailure>();
        foreach (Pair pair in Pairs.Where(p => accent || p.Fore != "accent"))
        {
            ThemeColor back = pair.Under is null ? palette[pair.Back] : palette[pair.Back].Over(palette[pair.Under]);
            double ratio = pair.Fore == Focus
                ? Math.Max(ThemeColor.Contrast(palette[Focus], back), ThemeColor.Contrast(palette[FocusInner], back))
                : ThemeColor.Contrast(palette[pair.Fore], back);

            if (ratio < pair.Minimum)
            {
                failures.Add(new ThemeFailure(
                    string.Create(CultureInfo.InvariantCulture, $"{pair.Words} is {Math.Floor(ratio * 10) / 10:0.0}:1 in {Of(mode)}; it needs {pair.Minimum:0.#}:1"),
                    [.. DependsOn(pair.Fore), .. DependsOn(pair.Back), .. DependsOn(pair.Under)]));
            }
        }

        // The mode is honest: light draws dark text on a lighter background, dark the reverse, as the native controls
        // Noto does not restyle do.
        bool lighter = palette["background"].Luminance > palette["text"].Luminance;
        if (lighter != (mode == ThemeMode.Light))
        {
            failures.Add(new ThemeFailure($"In {Of(mode)}, the background must be {(mode == ThemeMode.Light ? "lighter" : "darker")} than the text", ["text", "background"]));
        }

        return failures;
    }

    /// <summary>
    /// What a load draws (§4): the theme's <paramref name="set"/> for <paramref name="mode"/>, with each colour that makes
    /// a pair fail dropped for Noto's own, the foreground first, then the background, measured again after each. It
    /// ends: Noto's own colours pass every pair, so at worst the mode is entirely Noto's.
    /// </summary>
    /// <returns>The palette to draw, what is left of the theme's colours, and how many were dropped.</returns>
    public static (Dictionary<string, ThemeColor> Palette, Dictionary<string, ThemeColor> Kept, int Dropped) Fallback(
        IReadOnlyDictionary<string, ThemeColor> own, IReadOnlyDictionary<string, ThemeColor> set, ThemeMode mode)
    {
        var kept = new Dictionary<string, ThemeColor>(set);
        int dropped = 0;

        while (true)
        {
            Dictionary<string, ThemeColor> palette = Resolve(own, kept);
            string? drop = Problems(palette, kept, mode).Select(f => f.Properties.FirstOrDefault(kept.ContainsKey)).FirstOrDefault();
            if (drop is null)
            {
                return (palette, kept, dropped);
            }

            kept.Remove(drop);
            dropped++;
        }
    }

    /// <summary>The failures of a theme's colours (§4): those a colour the theme sets takes part in.</summary>
    public static IEnumerable<ThemeFailure> Problems(IReadOnlyDictionary<string, ThemeColor> palette, IReadOnlyDictionary<string, ThemeColor> set, ThemeMode mode)
    {
        ArgumentNullException.ThrowIfNull(set);

        return Measure(palette, mode, set.ContainsKey("accent")).Where(f => f.Properties.Any(set.ContainsKey));
    }

    private static string Of(ThemeMode mode) => mode == ThemeMode.Light ? "light" : "dark";

    /// <summary>The theme's properties a palette key is made from: a derived state from the background, card and text.</summary>
    private static string[] DependsOn(string? key) => key switch
    {
        null or Focus => [],
        Hover or Pressed or Selected or CardHover => ["text", "background", "card"],
        "code" or "highlight" => [key, "card"],
        _ => [key],
    };

    /// <summary>§4's table: text 4.5:1, non-text 3:1, disabled text 2:1.</summary>
    private static IEnumerable<Pair> Table()
    {
        const double Text = 4.5, NonText = 3, Disabled = 2;

        foreach ((string back, string words) in new[] { ("background", "the background"), ("card", "a card"), (CardHover, "a card under the pointer"), (Hover, "a row under the pointer"), (Pressed, "a pressed row"), (Selected, "the selected row") })
        {
            yield return new("text", back, null, Text, $"Text on {words}");
        }

        yield return new("textSecondary", "background", null, Text, "Secondary text on the background");
        yield return new("textSecondary", "card", null, Text, "Secondary text on a card");
        yield return new("link", "card", null, Text, "A link on a card");
        yield return new("quote", "card", null, Text, "A quote on a card");
        yield return new("danger", "background", null, Text, "A warning on the background");
        yield return new("text", "code", null, Text, "Text on code");
        yield return new("highlightText", "highlight", null, Text, "Highlighted text on its highlight");

        for (int n = 1; n <= ThemeFile.Notes.Count; n++)
        {
            foreach ((string fore, string words) in new[] { ("text", "Text"), ("textSecondary", "Secondary text"), ("link", "A link") })
            {
                yield return new(fore, $"note{n}.tint", "card", Text, $"{words} on note colour {n} on a card");
                yield return new(fore, $"note{n}.tint", CardHover, Text, $"{words} on note colour {n} on a card under the pointer");
            }
        }

        yield return new("checkmark", "card", null, NonText, "A task's checkbox on a card");
        yield return new("accent", "background", null, NonText, "The accent on the background");
        yield return new("accent", Selected, null, NonText, "The accent on the selected row");

        for (int n = 1; n <= ThemeFile.Notes.Count; n++)
        {
            yield return new($"note{n}.marker", "card", null, NonText, $"Note colour {n}'s marker on a card");
        }

        foreach ((string back, string words) in new[] { ("background", "the background"), ("card", "a card"), (Hover, "a row under the pointer"), (Pressed, "a pressed row"), (Selected, "the selected row") })
        {
            yield return new(Focus, back, null, NonText, $"The focus ring on {words}");
        }

        yield return new("textDisabled", "background", null, Disabled, "Disabled text on the background");
    }

    /// <summary>One rule: <paramref name="Fore"/> on <paramref name="Back"/> (laid over <paramref name="Under"/>, for a tint).</summary>
    private sealed record Pair(string Fore, string Back, string? Under, double Minimum, string Words);
}

/// <summary>A readability rule a palette breaks (ADR-023 §4).</summary>
/// <param name="Reason">In words, for the person who wrote the theme: <i>Secondary text on a card is 3.1:1 in dark; it needs 4.5:1</i>.</param>
/// <param name="Properties">The theme properties the pair is made of, foreground first: what a load drops, in order.</param>
public sealed record ThemeFailure(string Reason, IReadOnlyList<string> Properties);
