namespace Noto.Core.Workspace;

/// <summary>
/// A note text's line height or paragraph spacing (#234; parity D49, D50, J14, J15): three presets, as SideNotes offers.
/// </summary>
/// <remarks>
/// <see cref="Normal"/> is Noto's look before the settings existed, exactly, and the default. What each preset measures
/// is <see cref="NoteText"/>'s.
/// </remarks>
public enum TextSpacing
{
    /// <summary>Tighter than <see cref="Normal"/>.</summary>
    Compact = 0,

    /// <summary>Noto's look before the setting existed, and the default.</summary>
    Normal,

    /// <summary>Looser than <see cref="Normal"/>.</summary>
    Relaxed,
}
