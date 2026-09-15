using Noto.Core.Folders;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// The recycle bin, for notes (contract §11 Q6, invariant I2).
/// </summary>
/// <remarks>
/// <para>
/// A query, so it goes direct and stamps nothing (ADR-010, contract §4).
/// </para>
/// <para>
/// This is the <b>only</b> way to see deleted notes. The ordinary queries filter
/// <c>DeletedAt IS NULL</c> (I1) and take no flag to turn that off — I2 rejects
/// a <c>bool includeDeleted</c> outright as "the shape that eventually gets
/// passed <c>true</c> by accident".
/// </para>
/// <para>
/// It returns a plain list rather than a result: an empty bin is an ordinary
/// outcome, not a failure (contract §6).
/// </para>
/// </remarks>
public sealed class ListDeletedNotesQuery(INoteRepository notes)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));

    /// <summary>Every note in the bin; empty when there are none.</summary>
    public IReadOnlyList<Note> Execute() => _notes.ListDeleted();
}

/// <summary>
/// The recycle bin, for folders (contract §11 Q7, invariant I2).
/// </summary>
/// <remarks>
/// Separate from the note bin because notes and folders are separate entities
/// and a single union query would have to return one or the other badly.
/// </remarks>
public sealed class ListDeletedFoldersQuery(IFolderRepository folders)
{
    private readonly IFolderRepository _folders = folders ?? throw new ArgumentNullException(nameof(folders));

    /// <summary>Every folder in the bin; empty when there are none.</summary>
    public IReadOnlyList<Folder> Execute() => _folders.ListDeleted();
}
