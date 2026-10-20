using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;

namespace Noto.Core;

/// <summary>
/// What the recycle bin may and may not do (deletion-semantics §13), as decisions rather than prose.
/// </summary>
/// <remarks>
/// <para>
/// <b>I7: no active note is left where no surface reaches it.</b> The workspace shows notes only inside a live folder
/// (C1, ADR-016), so an active note whose folder is in the bin is invisible: it cannot be opened, moved or deleted
/// from the screen. <see cref="Reaches"/> states that once; every rule below that admits a note into a folder, or lets
/// a folder go, keeps it.
/// </para>
/// <para>
/// Each <c>Refuse…</c> method returns the failure a command must report, or <see langword="null"/> when the transition
/// is allowed. The repositories hold the same line inside their transactions, so a check and its write cannot be split.
/// </para>
/// </remarks>
public static class RecycleBinRules
{
    /// <summary>Whether an active note living in a folder in <paramref name="folder"/>'s state can be reached (I7).</summary>
    public static bool Reaches(FolderState folder) => folder == FolderState.Active;

    /// <summary>Permanently deleting a note: only one already in the bin (I5: a live note is deleted first).</summary>
    public static CommandFailure? RefusePurge(NoteId note, NoteLifecycle state) => state switch
    {
        NoteLifecycle.Deleted => null,
        NoteLifecycle.Active => CommandFailure.InvalidState($"Note '{note}' is not in the recycle bin."),
        _ => CommandFailure.NotFound($"No note '{note}'."),
    };

    /// <summary>
    /// Permanently deleting a folder, as far as its own state decides: only one already in the bin. Whether an active
    /// note still lives in it is decided inside the purge's transaction (<see cref="ActiveNotesRemain"/>).
    /// </summary>
    public static CommandFailure? RefusePurge(FolderId folder, FolderLifecycle state) => state switch
    {
        FolderLifecycle.Deleted => null,
        FolderLifecycle.Active => CommandFailure.InvalidState($"Folder '{folder}' is not in the recycle bin."),
        _ => CommandFailure.NotFound($"No folder '{folder}'."),
    };

    /// <summary>
    /// A binned folder that an active note still lives in is kept (I7): purging it would leave that note in no folder,
    /// where no surface shows it. The user restores the folder or moves the note first.
    /// </summary>
    public static CommandFailure ActiveNotesRemain(FolderId folder) =>
        CommandFailure.InvalidState($"Folder '{folder}' still holds an active note; restore the folder or move the note first.");

    /// <summary>Restoring a binned note into a chosen folder: the note must be in the bin, the folder live (I7).</summary>
    public static CommandFailure? RefuseRestoreInto(NoteId note, NoteLifecycle noteState, FolderId target, FolderState targetState)
    {
        switch (noteState)
        {
            case NoteLifecycle.Missing:
                return CommandFailure.NotFound($"No note '{note}'.");
            case NoteLifecycle.Active:
                return CommandFailure.InvalidState($"Note '{note}' is not in the recycle bin.");
            case NoteLifecycle.Deleted:
            default:
                break;
        }

        return targetState switch
        {
            FolderState.Missing => CommandFailure.NotFound($"No folder '{target}'."),
            _ when !Reaches(targetState) => CommandFailure.InvalidState($"Folder '{target}' is in the recycle bin and cannot receive a note."),
            _ => null,
        };
    }

    /// <summary>
    /// Restoring a binned note where it was, when that folder is itself in the bin (I7). Case C used to allow it; the
    /// note would come back invisible. The user restores the folder (Case D) or restores the note into another folder.
    /// </summary>
    public static CommandFailure FolderInBin(NoteId note) =>
        CommandFailure.InvalidState($"Note '{note}' was in a folder that is in the recycle bin; restore the folder or restore the note into another folder.");
}
