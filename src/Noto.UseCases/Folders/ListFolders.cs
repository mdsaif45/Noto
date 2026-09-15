using Noto.Core.Folders;

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
public sealed class ListFoldersQuery(IFolderRepository folders)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    public IReadOnlyList<Folder> Execute() => _folders.ListActive();
}
