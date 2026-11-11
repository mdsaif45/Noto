namespace Noto.Core.Workspace;

/// <summary>
/// The note text's font and size (F13, #154; parity D47, D48, D51, G41, G42, J12, J13): what may be stored, and the
/// steps the size shortcuts take.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fonts are a closed list of families Windows ships</b>, plus <see cref="DefaultFont"/>, the design system's own.
/// A list Noto can name is a list it can test; any installed font would need the system's font enumeration, and a font
/// that is gone later still falls back (D51): the view puts the design system's family after the chosen one.
/// </para>
/// <para>
/// <b>Sizes are steps, not any number</b>, so the shortcuts move through the same values the setting offers and a
/// stored size is always one of them.
/// </para>
/// </remarks>
public static class NoteText
{
    /// <summary>The design system's own family, stored as the empty string so it follows the design system.</summary>
    public const string DefaultFont = "";

    /// <summary>The size a note starts at: the design system's body size.</summary>
    public const double DefaultSize = 14;

    /// <summary>The families that may be chosen, besides <see cref="DefaultFont"/>.</summary>
    public static IReadOnlyList<string> Fonts { get; } = ["Segoe UI", "Calibri", "Cambria", "Georgia", "Verdana", "Consolas"];

    /// <summary>The sizes that may be chosen, in order: the steps the size shortcuts take.</summary>
    public static IReadOnlyList<double> Sizes { get; } = [10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24];

    /// <summary>Whether a font may be stored: <see cref="DefaultFont"/> or one of <see cref="Fonts"/>, exactly.</summary>
    public static bool IsFont(string font) => font == DefaultFont || Fonts.Contains(font, StringComparer.Ordinal);

    /// <summary>Whether a size may be stored: one of <see cref="Sizes"/>.</summary>
    public static bool IsSize(double size) => Sizes.Contains(size);

    /// <summary>
    /// The next size up (<paramref name="direction"/> &gt; 0) or down (&lt; 0) from <paramref name="size"/>, held at the
    /// ends. A size between steps moves to the nearest step in that direction.
    /// </summary>
    public static double Step(double size, int direction) => direction switch
    {
        > 0 => Sizes.FirstOrDefault(s => s > size, Sizes[^1]),
        < 0 => Sizes.LastOrDefault(s => s < size, Sizes[0]),
        _ => size,
    };
}
