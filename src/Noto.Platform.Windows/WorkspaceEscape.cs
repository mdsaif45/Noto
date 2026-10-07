using Noto.Core.Workspace;

namespace Noto.Platform.Windows;

/// <summary>Which workspace surface has the keyboard.</summary>
public enum WorkspaceSurface
{
    /// <summary>The folder list — the top level.</summary>
    FolderList,

    /// <summary>The notes of an entered folder.</summary>
    NoteList,

    /// <summary>The note editor.</summary>
    Editor,
}

/// <summary>What one press of Escape does.</summary>
public enum EscapeAction
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Leave the editor for the note list, saving unsaved text first.</summary>
    LeaveEditor,

    /// <summary>Leave the folder for the folder list.</summary>
    LeaveFolder,

    /// <summary>Hide the workspace, through the coordinator, saving unsaved text first.</summary>
    Hide,
}

/// <summary>
/// Escape's four behaviours (#16 slice 6; parity A12, G8, G51), as a pure
/// function so every mode is tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// "Leave" means one level back: the editor returns to its note list, a note
/// list returns to the folder list. Noto's editor is a level SideNotes does
/// not have, so leaving it is the same kind of step as leaving a folder.
/// </para>
/// <para>
/// An inline input — renaming or naming a folder — cancels itself on Escape
/// before any of this is consulted, in every mode.
/// </para>
/// <code>
///                       editor        note list     folder list
///   LeaveFolderOrHide   leave editor  leave folder  hide         (default)
///   LeaveFolder         leave editor  leave folder  nothing
///   Hide                hide          hide          hide
///   None                nothing       nothing       nothing
/// </code>
/// </remarks>
public static class WorkspaceEscape
{
    /// <summary>What Escape does on <paramref name="surface"/> under <paramref name="behavior"/>.</summary>
    public static EscapeAction Resolve(EscapeBehavior behavior, WorkspaceSurface surface) => behavior switch
    {
        EscapeBehavior.Hide => EscapeAction.Hide,
        EscapeBehavior.None => EscapeAction.None,
        EscapeBehavior.LeaveFolder or EscapeBehavior.LeaveFolderOrHide => surface switch
        {
            WorkspaceSurface.Editor => EscapeAction.LeaveEditor,
            WorkspaceSurface.NoteList => EscapeAction.LeaveFolder,
            WorkspaceSurface.FolderList when behavior == EscapeBehavior.LeaveFolderOrHide => EscapeAction.Hide,
            _ => EscapeAction.None,
        },
        _ => EscapeAction.None,
    };
}
