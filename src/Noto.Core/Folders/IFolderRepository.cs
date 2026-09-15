namespace Noto.Core.Folders;

/// <summary>
/// Persistence for folders.
/// </summary>
/// <remarks>
/// <para>
/// Its own repository rather than a method on <see cref="Notes.INoteRepository"/>:
/// design §11 calls for three repositories because there are three aggregates,
/// and contract §11 Q7 keeps notes and folders separate in the bin for the same
/// reason — they are different entities and a union return would serve neither.
/// </para>
/// <para>
/// <b>Slice 3 introduced it read-only; Slice 4 adds the commands.</b> The
/// methods here are the persistence operations the seven folder commands
/// actually need — not one method per use case. <c>PinFolder</c> and
/// <c>UnpinFolder</c> share <see cref="SetPinned"/>, because "set a flag" is
/// one storage operation and naming it twice would only make the two able to
/// drift apart.
/// </para>
/// </remarks>
public interface IFolderRepository
{
    /// <summary>
    /// The lifecycle state of a folder.
    /// </summary>
    /// <remarks>
    /// Three-valued because the contract needs a missing folder
    /// (<see cref="Commands.CommandFailureReason.NotFound"/>) separated from a
    /// binned one (<see cref="Commands.CommandFailureReason.InvalidState"/>,
    /// invariant I5). A boolean cannot carry that distinction, and returning
    /// the whole <see cref="Folder"/> would make every caller that only needs
    /// the precondition read nine columns.
    /// </remarks>
    FolderLifecycle GetLifecycle(FolderId id);

    /// <summary>
    /// Reads an <b>active</b> folder by id. A folder in the recycle bin is not
    /// found by this method.
    /// </summary>
    /// <returns>
    /// The folder, or <see langword="null"/> when no active row has that id.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Invariant I1 — active by default — applies here, so the
    /// <c>DeletedAt IS NULL</c> filter lives in the query rather than in each
    /// caller, exactly as it does for <see cref="Notes.INoteRepository.FindActive"/>.
    /// </para>
    /// <para>
    /// <see langword="null"/> means "no active row" and nothing else — this is
    /// the persistence seam, not the application API. The layer above turns it
    /// into <see cref="Commands.CommandFailureReason.NotFound"/> so that
    /// callers never see an overloaded null (contract §6).
    /// </para>
    /// <para>
    /// Needed because §4's no-op rule is decided by the folder's <i>current</i>
    /// state: a command that changes no row stamps nothing, so
    /// <c>PinFolder</c> must be able to see that the folder is already pinned.
    /// <see cref="GetLifecycle"/> cannot answer that — it reports only whether
    /// the row exists and is live.
    /// </para>
    /// </remarks>
    Folder? FindActive(FolderId id);

    /// <summary>
    /// Inserts a folder.
    /// </summary>
    void Add(Folder folder);

    /// <summary>
    /// Replaces a folder's name and stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Touches <c>Name</c> only.</b> <c>SortOrder</c>, <c>IsPinned</c> and
    /// <c>CreatedAt</c> are left exactly as they are (contract §11 row 13).
    /// </para>
    /// <para>
    /// A single-row update, so it needs no transaction (design §9). There is no
    /// uniqueness check anywhere: duplicate folder names are legal (Case G).
    /// </para>
    /// </remarks>
    void Rename(FolderId id, string name, DateTimeOffset updatedAt);

    /// <summary>
    /// Sets a folder's pinned flag and stamps <c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// <b>Touches <c>IsPinned</c> and nothing else.</b> Pinning is a display
    /// partition (O2) laid over the sequence, not a rewrite of it — the same
    /// rule as notes. A pin that moved the folder would destroy the position
    /// unpinning is supposed to return it to.
    /// </remarks>
    void SetPinned(FolderId id, bool isPinned, DateTimeOffset updatedAt);

    /// <summary>
    /// Repositions a folder within the folder collection, in <b>one
    /// transaction</b>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the folder already occupies that position —
    /// the no-op case, which writes nothing and stamps nothing (contract §5).
    /// </returns>
    /// <remarks>
    /// Atomic with any renormalisation it triggers (design §9). The folder
    /// collection is a single scope, so there is no scope argument.
    /// </remarks>
    bool Reorder(FolderId id, FolderPlacement placement, DateTimeOffset updatedAt);

    /// <summary>
    /// Case A/B — moves a folder and its <b>currently-active</b> notes into the
    /// recycle bin, in <b>one transaction</b> with <b>one timestamp</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cascade filters <c>DeletedAt IS NULL</c>. That filter is the whole
    /// of Case B: without it, a note the user had already deleted would have
    /// its original <c>DeletedAt</c> overwritten by this operation's timestamp,
    /// silently rewriting when the user deleted it.
    /// </para>
    /// <para>
    /// Notes in other folders and at root are never touched (I3).
    /// </para>
    /// </remarks>
    void SoftDeleteWithNotes(FolderId id, DateTimeOffset deletedAt);

    /// <summary>
    /// Case D — brings a folder and <b>every still-deleted note pointing at
    /// it</b> back out of the recycle bin, in <b>one transaction</b> with
    /// <b>one timestamp</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every</b> deleted note with a matching <c>FolderId</c> is restored,
    /// including one the user deleted individually before the folder was
    /// deleted. That is the contract's Case D, stated as SQL in §8:
    /// <c>WHERE FolderId = @F AND DeletedAt IS NOT NULL</c>.
    /// </para>
    /// <para>
    /// Restoring only the notes this folder's deletion had cascaded would
    /// require recording <i>why</i> each note was deleted —
    /// a <c>DeletedWithFolderId</c> column, which deletion-semantics §5
    /// explicitly forbids. <c>DeletedAt</c> says whether, <c>FolderId</c> says
    /// where; nothing records why, and nothing needs to.
    /// </para>
    /// <para>
    /// Notes belonging to other folders, and deleted notes at root, are never
    /// touched (I3).
    /// </para>
    /// </remarks>
    void RestoreWithNotes(FolderId id, DateTimeOffset updatedAt);

    /// <summary>
    /// The highest <c>SortOrder</c> among the <b>active</b> folders, or
    /// <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// Feeds the O3 end-insertion rule — <c>max + 1</c> — so a new folder lands
    /// after the existing ones. Only active folders count: deleted rows keep
    /// their <c>SortOrder</c> but never participate in ordering (I6).
    /// </remarks>
    double? MaxSortOrder();

    /// <summary>
    /// Whether a folder exists and is <b>active</b>.
    /// </summary>
    /// <remarks>
    /// Validates a reorder target. The folder collection is one scope, so —
    /// unlike the note equivalent — there is no scope to compare against; being
    /// active is the whole condition, because deleted rows never participate in
    /// ordering (I6).
    /// </remarks>
    bool IsActiveSibling(FolderId id);

    /// <summary>
    /// Every folder in the recycle bin.
    /// </summary>
    /// <remarks>
    /// The I2 explicit-bin method for folders. As with notes, there is no
    /// <c>includeDeleted</c> flag anywhere on the ordinary queries.
    /// </remarks>
    IReadOnlyList<Folder> ListDeleted();
}

/// <summary>
/// Whether a folder exists, and whether it is live.
/// </summary>
/// <remarks>
/// Deliberately the same three states as <see cref="Notes.FolderState"/>, which
/// answers the same question for the note repository's own preconditions. They
/// are not merged because the two interfaces are consumed independently and
/// neither project should have to reference the other's enum to read its own
/// contract.
/// </remarks>
public enum FolderLifecycle
{
    /// <summary>No folder has that id.</summary>
    Missing = 0,

    /// <summary>The folder exists and is live.</summary>
    Active,

    /// <summary>The folder exists but is in the recycle bin — inert (I5).</summary>
    Deleted,
}
