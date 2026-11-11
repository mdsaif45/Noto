namespace Noto.Core.Activation;

/// <summary>
/// What a global hotkey does (parity G1, G2, G4): each is rebindable, and every one but show/hide is off by default
/// (principle 7).
/// </summary>
public enum GlobalAction
{
    /// <summary>Shows and hides Noto (G1).</summary>
    ShowHide,

    /// <summary>Shows Noto with a new note ready to type in (B2, G2, F17).</summary>
    NewNote,

    /// <summary>Shows Noto with a new note holding the clipboard's text or picture (B3, D43, G4, F18).</summary>
    NoteFromClipboard,
}
