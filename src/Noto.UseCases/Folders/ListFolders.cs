using Noto.Core.Folders;
using Noto.Core.Notes;

namespace Noto.UseCases.Folders;

/// <summary>
/// Q3 — the active folders, in display order (contract §11 Q3, §5a).
/// </summary>
/// <remarks>
/// O2 in full: pinned folders first, then <c>SortOrder</c>, then <c>Id</c>.
/// Deleted folders are excluded by I1 and reached only through
/// <c>ListDeletedFolders</c> (I2). No failure case — §11 gives this query an
/// empty failure column, and an empty collection is an answer.
/// </remarks>
public sealed class ListFoldersQuery(IFolderRepository folders, INoteRepository notes)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    public IReadOnlyList<Folder> Execute() => _folders.ListActive();

    /// <summary>How many active notes each folder holds, for the folder list's rows (#147). Absent means none.</summary>
    public IReadOnlyDictionary<FolderId, int> NoteCounts() => _notes.CountActiveByFolder();
}
