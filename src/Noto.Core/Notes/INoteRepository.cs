using Noto.Core.Folders;

namespace Noto.Core.Notes;

/// <summary>
/// Persistence for notes.
/// </summary>
/// <remarks>
/// <para>
/// The interface lives in <c>Noto.Core</c> and its implementation in
/// <c>Noto.Infrastructure</c> (core-note-engine-design.md §11), so the domain
/// states what it needs and knows nothing about how it is stored. Core targets
/// plain <c>net9.0</c> and has no storage package reference, which makes a
/// SQLite type here a build error rather than a review comment.
/// </para>
/// <para>
/// Three focused repositories rather than a generic <c>IRepository&lt;T&gt;</c>:
/// notes, folders and tags do not have the same operations, and pretending they
/// do produces a lowest-common-denominator interface nobody can read.
/// </para>
/// <para>
/// <b>Slice 1 only.</b> Methods arrive with the commands that need them; an
/// interface written ahead of its callers is a guess.
/// </para>
/// </remarks>
public interface INoteRepository
{
    /// <summary>
    /// Inserts a note.
    /// </summary>
    void Add(Note note);

    /// <summary>
    /// Reads an <b>active</b> note by id. A note in the recycle bin is not
    /// found by this method.
    /// </summary>
    /// <returns>
    /// The note, or <see langword="null"/> when no active row has that id.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Invariant I1 — active by default — applies here, so the
    /// <c>DeletedAt IS NULL</c> filter lives in the query rather than in each
    /// caller. The recycle bin is reached only through
    /// <c>ListDeletedNotes</c> (I2), which arrives in Slice 3.
    /// </para>
    /// <para>
    /// <see langword="null"/> means "no active row" and nothing else — this is
    /// the persistence seam, not the application API. The layer above turns it
    /// into <see cref="Commands.CommandFailureReason.NotFound"/> so that
    /// callers never see an overloaded null (contract §6).
    /// </para>
    /// </remarks>
    Note? FindActive(NoteId id);

    /// <summary>
    /// The lifecycle state of a folder, for validating a note's destination.
    /// </summary>
    /// <remarks>
    /// Three-valued rather than a boolean, because the contract requires
    /// <c>CreateNote</c> to distinguish a folder that does not exist
    /// (<see cref="Commands.CommandFailureReason.NotFound"/>) from one that is
    /// in the recycle bin (<see cref="Commands.CommandFailureReason.InvalidState"/>,
    /// invariant I5). A boolean cannot carry that distinction.
    /// </remarks>
    FolderState GetFolderState(FolderId id);

    /// <summary>
    /// The highest <c>SortOrder</c> among the <b>active</b> notes of a scope,
    /// or <see langword="null"/> when the scope holds none.
    /// </summary>
    /// <param name="folderId">
    /// The scope. <see langword="null"/> is the root scope, which is its own
    /// scope and not a catch-all (ordering rule O1).
    /// </param>
    /// <remarks>
    /// Feeds the O3 end-insertion rule — <c>max + 1</c> — so a new note lands
    /// after its siblings. Only active notes count: deleted rows keep their
    /// <c>SortOrder</c> but never participate in ordering (invariant I6).
    /// </remarks>
    double? MaxSortOrder(FolderId? folderId);
}

/// <summary>
/// Whether a folder exists, and whether it is live.
/// </summary>
public enum FolderState
{
    /// <summary>No folder has that id.</summary>
    Missing = 0,

    /// <summary>The folder exists and is live.</summary>
    Active,

    /// <summary>The folder exists but is in the recycle bin.</summary>
    Deleted,
}
