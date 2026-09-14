namespace Noto.Core.Notes;

/// <summary>
/// The six persisted note palette keys (ADR-011).
/// </summary>
/// <remarks>
/// <para>
/// Parity B15 requires six colours plus none, and G23/G24 bind them to
/// <c>Ctrl+1</c>–<c>Ctrl+6</c> and <c>Ctrl+0</c>. ADR-011 fixes the persisted
/// identifiers as <c>note1</c>…<c>note6</c>, with <see langword="null"/> — not a
/// seventh key — meaning no colour.
/// </para>
/// <para>
/// The names are ordinal rather than hues on purpose: the stored value is a
/// palette <i>position</i>, and the theme decides what that position is painted
/// as. A key called <c>yellow</c> would be a lie in a theme that renders
/// position 1 as something else, and would strand every note written under the
/// old theme.
/// </para>
/// </remarks>
public static class NoteColor
{
    /// <summary>
    /// The six valid keys, in palette order — <c>note1</c> is <c>Ctrl+1</c>.
    /// </summary>
    /// <remarks>
    /// Ordinal, so the collection's order <i>is</i> the shortcut order. The set
    /// is closed: anything outside it is rejected rather than stored, which is
    /// what makes the contract's "unknown colour key → InvalidInput" rule
    /// evaluable.
    /// </remarks>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "note1",
        "note2",
        "note3",
        "note4",
        "note5",
        "note6",
    ];

    /// <summary>
    /// Whether a value may be persisted in <c>ColorKey</c>.
    /// </summary>
    /// <param name="colorKey">
    /// A palette key, or <see langword="null"/> to clear the colour.
    /// </param>
    /// <remarks>
    /// <see langword="null"/> is valid because it is how "no colour" is
    /// represented (<c>Ctrl+0</c>). Comparison is ordinal and case-sensitive:
    /// the keys are stored identifiers, not user-facing text, so accepting
    /// <c>NOTE1</c> would put two spellings of one colour into the database.
    /// </remarks>
    public static bool IsValid(string? colorKey) =>
        colorKey is null || Keys.Contains(colorKey, StringComparer.Ordinal);
}
