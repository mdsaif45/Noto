using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;

namespace Noto.UseCases.Workspace;

/// <summary>What one Recycle Bin entry is.</summary>
public enum RecycleBinEntryKind
{
    Folder,
    Note,
}

/// <summary>
/// One entry in the Recycle Bin (workspace-management.md, "B5 precisely"): a binned folder, or a binned note.
/// </summary>
/// <param name="Id">The folder's or note's id.</param>
/// <param name="Title">The folder's name, or the note's title (empty for an untitled note).</param>
/// <param name="FolderName">For a note, its folder's name; empty for a folder or a note whose folder is gone.</param>
/// <param name="NoteCount">For a folder, how many of its notes are in the bin with it.</param>
/// <param name="CanRestore">Whether plain Restore is offered: always for a folder; for a note, only into a live folder (I7).</param>
/// <param name="FolderInBin">For a note, whether its folder is itself in the bin.</param>
public sealed record RecycleBinEntry(
    RecycleBinEntryKind Kind,
    string Id,
    string Title,
    string FolderName,
    int NoteCount,
    DateTimeOffset DeletedAt,
    bool CanRestore,
    bool FolderInBin);

/// <summary>
/// The Recycle Bin's session (B7, B23, C11): what is in the bin, and the engine's own commands to restore, purge or
/// empty it. Framework-free, so the window only shows entries and asks before what cannot be undone.
/// </summary>
/// <remarks>
/// Nothing here decides what a restore or a purge does: those are the P1.1 handlers' (deletion-semantics §13). This
/// only lists, and names which command an entry's action is.
/// </remarks>
public sealed class RecycleBin(
    ListDeletedFoldersQuery deletedFolders,
    ListDeletedNotesQuery deletedNotes,
    ListFoldersQuery activeFolders,
    ListNotesInFolderQuery notesInFolder,
    CommandDispatcher commands)
{
    private readonly ListDeletedFoldersQuery _deletedFolders = deletedFolders ?? throw new ArgumentNullException(nameof(deletedFolders));
    private readonly ListDeletedNotesQuery _deletedNotes = deletedNotes ?? throw new ArgumentNullException(nameof(deletedNotes));
    private readonly ListFoldersQuery _activeFolders = activeFolders ?? throw new ArgumentNullException(nameof(activeFolders));
    private readonly ListNotesInFolderQuery _notesInFolder = notesInFolder ?? throw new ArgumentNullException(nameof(notesInFolder));
    private readonly CommandDispatcher _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    /// <summary>How many active notes a folder's deletion would take to the bin with it: the confirmation names them.</summary>
    public int NotesIn(FolderId folder) => _notesInFolder.Execute(folder).Count;

    /// <summary>
    /// Moves a folder and its active notes to the bin (C11, Cases A and B): soft, one timestamp, nothing destroyed. The
    /// window has asked first; a cancelled confirmation never reaches here.
    /// </summary>
    public CommandResult DeleteFolder(FolderId folder) => _commands.Dispatch(new DeleteFolder(folder));

    /// <summary>Every binned folder and note, most recently deleted first; at an equal time, folders first.</summary>
    public IReadOnlyList<RecycleBinEntry> Entries()
    {
        IReadOnlyList<Folder> binnedFolders = _deletedFolders.Execute();
        IReadOnlyList<Note> binnedNotes = _deletedNotes.Execute();
        IReadOnlyList<Folder> live = _activeFolders.Execute();

        IEnumerable<RecycleBinEntry> folders = binnedFolders.Select(f => new RecycleBinEntry(
            RecycleBinEntryKind.Folder,
            f.Id.Value,
            f.Name,
            string.Empty,
            binnedNotes.Count(n => n.FolderId == f.Id),
            f.DeletedAt!.Value,
            CanRestore: true,
            FolderInBin: false));

        IEnumerable<RecycleBinEntry> notes = binnedNotes.Select(n =>
        {
            Folder? liveFolder = live.FirstOrDefault(f => f.Id == n.FolderId);
            Folder? binnedFolder = binnedFolders.FirstOrDefault(f => f.Id == n.FolderId);
            return new RecycleBinEntry(
                RecycleBinEntryKind.Note,
                n.Id.Value,
                n.Title,
                liveFolder?.Name ?? binnedFolder?.Name ?? string.Empty,
                0,
                n.DeletedAt!.Value,
                CanRestore: liveFolder is not null,
                FolderInBin: binnedFolder is not null);
        });

        return
        [
            .. folders.Concat(notes)
                .OrderByDescending(e => e.DeletedAt)
                .ThenBy(e => e.Kind)
                .ThenBy(e => e.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>The live folders a note can be restored into.</summary>
    public IReadOnlyList<Folder> Destinations() => _activeFolders.Execute();

    /// <summary>Restores an entry where it was: a folder with its notes (Case D), a note into its live folder.</summary>
    public CommandResult Restore(RecycleBinEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind == RecycleBinEntryKind.Folder
            ? _commands.Dispatch(new RestoreFolder(FolderId.From(entry.Id)))
            : _commands.Dispatch(new RestoreNote(NoteId.From(entry.Id)));
    }

    /// <summary>Restores a binned note into a live folder of the user's choice (P1.1).</summary>
    public CommandResult RestoreTo(RecycleBinEntry entry, FolderId folder)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind == RecycleBinEntryKind.Note
            ? _commands.Dispatch(new RestoreNoteToFolder(NoteId.From(entry.Id), folder))
            : CommandResult.Failed(CommandFailure.InvalidInput("Only a note is restored into another folder."));
    }

    /// <summary>Deletes an entry permanently: the window has asked first. A folder takes its binned notes with it.</summary>
    public CommandResult Purge(RecycleBinEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind == RecycleBinEntryKind.Folder
            ? _commands.Dispatch(new PurgeFolder(FolderId.From(entry.Id)))
            : _commands.Dispatch(new PurgeNote(NoteId.From(entry.Id)));
    }

    /// <summary>Empties the bin: the window has asked first. What was kept, and why, is in the result (I7).</summary>
    public CommandResult<RecycleBinPurge> Empty() => _commands.Dispatch(new EmptyRecycleBin());
}
