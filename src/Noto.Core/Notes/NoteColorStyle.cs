namespace Noto.Core.Notes;

/// <summary>
/// How a coloured card shows its colour (#191; parity B15, F10, J23): SideNotes offers the same two styles.
/// </summary>
/// <remarks>
/// A display choice only: a note's colour (<see cref="NoteColor"/>) is stored and named the same in both, so its
/// meaning, and what assistive technology says about it, never depends on the style.
/// </remarks>
public enum NoteColorStyle
{
    /// <summary>The card is tinted and marked along its leading edge: Noto's look before the setting existed, and the default.</summary>
    Background = 0,

    /// <summary>Only the marker: the card keeps an uncoloured card's fill, with a coloured bar along its edge.</summary>
    Bar,
}
