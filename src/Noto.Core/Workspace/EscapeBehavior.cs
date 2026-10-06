namespace Noto.Core.Workspace;

/// <summary>
/// What the Escape key does in the workspace (parity A12, G8, G51).
/// </summary>
/// <remarks>
/// <para>
/// SideNotes' four modes, with its default (feature inventory A12,
/// CONFIRMED): <see cref="LeaveFolderOrHide"/> — the first press goes back a
/// level, a press at the folder list hides the workspace.
/// </para>
/// <para>
/// Stored by member name in the <c>workspace.escape</c> setting, so the order
/// of the members is not a contract. An inline input (renaming or naming a
/// folder) always cancels itself first, whatever the mode.
/// </para>
/// </remarks>
public enum EscapeBehavior
{
    /// <summary>Go back a level; at the folder list, hide. The default.</summary>
    LeaveFolderOrHide,

    /// <summary>Go back a level; at the folder list, nothing.</summary>
    LeaveFolder,

    /// <summary>Hide, from anywhere.</summary>
    Hide,

    /// <summary>Nothing.</summary>
    None,
}
