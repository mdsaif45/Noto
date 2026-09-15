using Noto.Core.Folders;
using Noto.Core.Tags;

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
    /// The lifecycle state of a note.
    /// </summary>
    /// <remarks>
    /// <see cref="FindActive"/> cannot answer this: it applies I1, so a deleted
    /// note and a missing one both come back <see langword="null"/>. The
    /// contract needs them separated — a mutation on a deleted note is
    /// <see cref="Commands.CommandFailureReason.InvalidState"/> (I5), not
    /// <see cref="Commands.CommandFailureReason.NotFound"/>.
    /// </remarks>
    NoteLifecycle GetLifecycle(NoteId id);

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

    /// <summary>
    /// Replaces a note's content and stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// A single-row update, so it needs no transaction (design §9). The caller
    /// has already established that the note is active.
    /// </remarks>
    void UpdateContent(NoteId id, string content, DateTimeOffset updatedAt);

    /// <summary>
    /// Moves a note to another scope and gives it a position there, in
    /// <b>one transaction</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O4 and design §9: the folder change and the new <c>SortOrder</c> are one
    /// atomic unit. Committing the move without the position would leave the
    /// note in the target scope at a value that means nothing there.
    /// </para>
    /// <para>
    /// The placement may trigger renormalisation (O6), which rewrites the
    /// target scope inside the same transaction.
    /// </para>
    /// </remarks>
    void Move(NoteId id, FolderId? targetFolderId, NotePlacement placement, DateTimeOffset updatedAt);

    /// <summary>
    /// Repositions a note within its current scope, in <b>one transaction</b>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the note already occupies that position —
    /// the no-op case, which writes nothing and stamps nothing (contract §5).
    /// </returns>
    /// <remarks>
    /// Atomic with any renormalisation it triggers (design §9).
    /// </remarks>
    bool Reorder(NoteId id, NotePlacement placement, DateTimeOffset updatedAt);

    /// <summary>
    /// Whether a note is an <b>active</b> member of the given scope.
    /// </summary>
    /// <remarks>
    /// Validates a reorder target: it must be an active sibling in the same
    /// scope (contract §5), and deleted rows never participate in ordering (I6).
    /// </remarks>
    bool IsActiveSiblingIn(NoteId id, FolderId? folderId);

    /// <summary>
    /// Sets a note's pinned flag and stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// <b>Touches <c>IsPinned</c> and nothing else.</b> Pinning is a display
    /// partition (O2) laid over the sequence, not a rewrite of it — which is
    /// what makes the contract's promise that <c>UnpinNote</c> "returns to
    /// <c>SortOrder</c> position" possible. A pin that moved the note to the
    /// top of the scope would destroy the position it is supposed to return to.
    /// </remarks>
    void SetPinned(NoteId id, bool isPinned, DateTimeOffset updatedAt);

    /// <summary>
    /// Sets a note's folded flag and stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// Folded-ness is user-authored note state that must persist identically in
    /// every surface (design §5, parity B12), not a rendering coordinate.
    /// </remarks>
    void SetFolded(NoteId id, bool isFolded, DateTimeOffset updatedAt);

    /// <summary>
    /// Sets a note's palette key, or clears it, and stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <param name="colorKey">
    /// One of <see cref="NoteColor.Keys"/>, or <see langword="null"/> to clear.
    /// The caller has already validated it.
    /// </param>
    void SetColor(NoteId id, string? colorKey, DateTimeOffset updatedAt);

    /// <summary>
    /// Moves a note into the recycle bin: sets <c>DeletedAt</c>, and stamps
    /// <c>UpdatedAt</c> with the same instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A soft delete.</b> The row stays, which is what lets the bin list it
    /// and restore bring it back (B23, ADR-002 — data cannot be recovered from
    /// a server, so it must not be destroyed locally).
    /// </para>
    /// <para>
    /// Siblings are <b>not</b> renumbered. O5: deleting leaves gaps, and gaps
    /// are harmless. The deleted row also keeps its own <c>SortOrder</c>, which
    /// is what makes restore return it roughly where it was (I6).
    /// </para>
    /// </remarks>
    void SoftDelete(NoteId id, DateTimeOffset deletedAt);

    /// <summary>
    /// Brings a note back out of the recycle bin: clears <c>DeletedAt</c> and
    /// stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// Touches those two columns only. The note's <c>SortOrder</c> was never
    /// disturbed, so it returns to roughly its old position without any
    /// placement logic (I6). Its <c>FolderId</c> is untouched too — Case C
    /// deliberately allows a restored note to point at a folder that is itself
    /// still deleted.
    /// </remarks>
    void Restore(NoteId id, DateTimeOffset updatedAt);

    /// <summary>
    /// Every note in the recycle bin.
    /// </summary>
    /// <remarks>
    /// The I2 explicit-bin method: the only way to see deleted notes. There is
    /// deliberately no <c>includeDeleted</c> flag on the ordinary queries —
    /// that is "the shape that eventually gets passed <c>true</c> by accident".
    /// </remarks>
    IReadOnlyList<Note> ListDeleted();

    /// <summary>
    /// The <b>active</b> notes of one scope, in display order (Q2).
    /// </summary>
    /// <param name="folderId">
    /// The scope. <see langword="null"/> is the root scope, which is its own
    /// scope and not a catch-all (ordering rule O1) — a root note belongs to no
    /// folder's list, and no folder's notes appear at root.
    /// </param>
    /// <returns>
    /// The notes in O2 order — pinned first, then <c>SortOrder</c>, then
    /// <c>Id</c> — or an empty list when the scope holds none. An empty scope
    /// is not a failure (contract §11 Q2).
    /// </returns>
    /// <remarks>
    /// Invariant I1 applies: deleted notes are excluded here, and are reached
    /// only through <see cref="ListDeleted"/> (I2). I6 follows from the same
    /// filter — a deleted row keeps its <c>SortOrder</c> but never participates
    /// in ordering.
    /// </remarks>
    IReadOnlyList<Note> ListInFolder(FolderId? folderId);

    /// <summary>
    /// The <b>active</b> notes carrying a tag, most recently updated first (Q5).
    /// </summary>
    /// <returns>
    /// The notes in <c>UpdatedAt DESC</c> order (contract §5a, U13b), or an
    /// empty list when the tag has none — including when no such tag exists.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Not ordered by <c>SortOrder</c>.</b> Notes reached through a tag come
    /// from many folders, and <c>SortOrder</c> is scoped per folder (O1), so
    /// values from different scopes are not comparable. <c>UpdatedAt</c> is the
    /// one field every note carries that is comparable across scopes.
    /// </para>
    /// <para>
    /// <b>No secondary tie-break.</b> Two notes can share an <c>UpdatedAt</c> —
    /// §4 gives every row in one transaction the same timestamp — and the
    /// contract deliberately does not say how those order relative to each
    /// other (§5a). Callers must not depend on it.
    /// </para>
    /// <para>
    /// Membership comes solely from <c>NoteTags</c>; I1 excludes deleted notes.
    /// </para>
    /// </remarks>
    IReadOnlyList<Note> ListForTag(TagId tagId);
}

/// <summary>
/// Whether a note exists, and whether it is live.
/// </summary>
public enum NoteLifecycle
{
    /// <summary>No note has that id.</summary>
    Missing = 0,

    /// <summary>The note exists and is live.</summary>
    Active,

    /// <summary>The note exists but is in the recycle bin — inert (I5).</summary>
    Deleted,
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
