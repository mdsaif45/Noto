using Noto.Core.Notes;

namespace Noto;

/// <summary>
/// The note palette as shown (F7, #150): each stored key's name, in palette order, and its state on a card
/// (NoteRow.xaml's ColorStates). The key is what is stored; the name and the paint are the theme's (ADR-011).
/// </summary>
internal static class NotePalette
{
    /// <summary>The colours, in <see cref="NoteColor.Keys"/> order: <c>note1</c> is Ctrl+1.</summary>
    public static IReadOnlyList<(string Key, string Name)> Colors { get; } =
    [
        ("note1", "Yellow"),
        ("note2", "Green"),
        ("note3", "Blue"),
        ("note4", "Purple"),
        ("note5", "Pink"),
        ("note6", "Grey"),
    ];

    /// <summary>A key's name, or <see langword="null"/> for no colour or a key the palette does not have.</summary>
    public static string? NameOf(string? key) => Colors.FirstOrDefault(c => c.Key == key).Name;

    /// <summary>The card's colour state: <c>Note1</c> to <c>Note6</c>, or <c>NoColor</c>.</summary>
    public static string StateOf(string? key) => NameOf(key) is null ? "NoColor" : "Note" + key![^1];
}
