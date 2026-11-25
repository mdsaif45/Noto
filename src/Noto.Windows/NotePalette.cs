using Noto.Core.Notes;

namespace Noto;

/// <summary>
/// The note palette as shown (F7, #150): each stored key's name, in palette order, and its state on a card
/// (NoteRow.xaml's ColorStates). The key is what is stored; the name and the paint are the theme's (ADR-011).
/// </summary>
internal static class NotePalette
{
    private static readonly (string Key, string Name)[] Own =
    [
        ("note1", "Yellow"),
        ("note2", "Green"),
        ("note3", "Blue"),
        ("note4", "Purple"),
        ("note5", "Pink"),
        ("note6", "Grey"),
    ];

    // A theme's names for each position (#269, ADR-023 §2), light and dark; null keeps Noto's own.
    private static IReadOnlyList<string?> _light = [];
    private static IReadOnlyList<string?> _dark = [];

    /// <summary>Whether the drawer is dark, so a theme's dark names are the ones said; set with the mode.</summary>
    internal static bool Dark { get; set; }

    /// <summary>
    /// The colours, in <see cref="NoteColor.Keys"/> order: <c>note1</c> is Ctrl+1. A theme that repaints a colour may
    /// rename it (#269), so a screen reader never calls a red note Yellow; the key never changes.
    /// </summary>
    public static IReadOnlyList<(string Key, string Name)> Colors =>
        [.. Own.Select((c, i) => (c.Key, (Dark ? _dark : _light).ElementAtOrDefault(i) ?? c.Name))];

    /// <summary>A theme's names for the colours, light and dark, by position; empty lists for Noto's own (#269).</summary>
    internal static void Rename(IReadOnlyList<string?> light, IReadOnlyList<string?> dark) => (_light, _dark) = (light, dark);

    /// <summary>A key's name, or <see langword="null"/> for no colour or a key the palette does not have.</summary>
    public static string? NameOf(string? key) => Colors.FirstOrDefault(c => c.Key == key).Name;

    /// <summary>The card's colour state: <c>Note1</c> to <c>Note6</c>, or <c>NoColor</c>.</summary>
    public static string StateOf(string? key) => NameOf(key) is null ? "NoColor" : "Note" + key![^1];

    /// <summary>The card's colour style state (#191): <c>BarStyle</c> hides the tint, <c>BackgroundStyle</c> shows it.</summary>
    public static string StateOf(NoteColorStyle style) => style == NoteColorStyle.Bar ? "BarStyle" : "BackgroundStyle";
}
