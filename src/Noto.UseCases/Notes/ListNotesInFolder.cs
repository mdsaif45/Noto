using Noto.Core.Folders;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Q2 — the active notes of one scope, in display order
/// (contract §11 Q2, §5a).
/// </summary>
/// <remarks>
/// <para>
/// No <c>CommandResult</c>: §11 gives this query an empty failure column. An
/// empty scope returns an empty list, which is an answer rather than a failure —
/// a folder with no notes is an ordinary state, and so is a folder that does
/// not exist.
/// </para>
/// <para>
/// Ordering is O2 in full — pinned first, then <c>SortOrder</c>, then <c>Id</c>
/// — and lives in the repository's SQL, where the index can serve it.
/// </para>
/// </remarks>
public sealed class ListNotesInFolderQuery(INoteRepository notes)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    /// <param name="folderId">
    /// The scope. <see langword="null"/> is the root scope, its own scope and
    /// not a catch-all (O1).
    /// </param>
    public IReadOnlyList<Note> Execute(FolderId? folderId) => _notes.ListInFolder(folderId);
}
