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
    /// Reads a note by id, <b>including</b> one in the recycle bin.
    /// </summary>
    /// <returns>The note, or <see langword="null"/> when no row has that id.</returns>
    /// <remarks>
    /// <para>
    /// <see langword="null"/> here means "no such row" and nothing else — this
    /// is the persistence seam, not the application API. The command layer
    /// turns it into <see cref="Commands.CommandFailureReason.NotFound"/> so
    /// that callers never see an overloaded null (contract §6).
    /// </para>
    /// <para>
    /// Deleted notes are returned so that the operations which legitimately act
    /// on them — restore, and the precondition checks that must answer "deleted"
    /// rather than "missing" — can see them. Invariant I1 is enforced by the
    /// query methods that list notes, not by this one.
    /// </para>
    /// </remarks>
    Note? Find(NoteId id);

    /// <summary>
    /// Whether an <b>active</b> folder with this id exists.
    /// </summary>
    /// <remarks>
    /// Used to validate a note's destination on create. It answers about the
    /// folder rather than returning one, because Slice 1 needs the fact, not
    /// the entity.
    /// </remarks>
    bool ActiveFolderExists(FolderId id);
}
