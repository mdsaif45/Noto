using Noto.Core.Notes;

namespace Noto.Core.Tags;

/// <summary>
/// Persistence for tags and the note–tag relationship.
/// </summary>
/// <remarks>
/// <para>
/// Its own repository, the third of the three the design calls for (design
/// §11). The <c>NoteTags</c> join lives here rather than on
/// <see cref="INoteRepository"/>: it is the tag aggregate's table, and putting
/// it on the note repository would give notes knowledge of tags for no gain.
/// </para>
/// <para>
/// <b>Two shape differences from the other repositories, both forced by the
/// contract rather than chosen.</b> No method takes a <c>DateTimeOffset</c>,
/// because §4 says every tag command stamps nothing — a timestamp parameter
/// would be an invitation to stamp. And there is no <c>GetLifecycle</c>,
/// because a tag has no lifecycle: <c>Tags</c> has no <c>DeletedAt</c>, so a
/// tag exists or it does not (§8, the only hard delete).
/// </para>
/// </remarks>
public interface ITagRepository
{
    /// <summary>
    /// The id of the tag with this name, compared case-insensitively, or
    /// <see langword="null"/> when no tag has it.
    /// </summary>
    /// <param name="name">
    /// An <b>already-normalised</b> name (contract §7a). The comparison is only
    /// as correct as its input: passing an untrimmed name here would miss the
    /// collision between <c>" Work"</c> and <c>"Work"</c>, which is the whole
    /// reason §7a exists.
    /// </param>
    /// <remarks>
    /// Returns the id rather than a bool so <c>RenameTag</c> can tell "this
    /// name belongs to another tag" from "this name is my own current name" —
    /// the second is a legal no-collision rename, and a bool cannot express it.
    /// </remarks>
    TagId? FindIdByName(string name);

    /// <summary>
    /// Reads a tag by id, or <see langword="null"/> when none has it.
    /// </summary>
    /// <remarks>
    /// No active/deleted distinction to make, unlike the note and folder
    /// equivalents: a tag row exists or it has been hard-deleted.
    /// </remarks>
    Tag? Find(TagId id);

    /// <summary>
    /// Inserts a tag.
    /// </summary>
    /// <remarks>
    /// The caller has already normalised the name and established that it does
    /// not collide.
    /// </remarks>
    void Add(Tag tag);

    /// <summary>
    /// Replaces a tag's name.
    /// </summary>
    /// <param name="name">An already-normalised name (contract §7a).</param>
    /// <remarks>
    /// <b>No timestamp parameter.</b> Contract §11 row 20: replace <c>Name</c>,
    /// "stamps nothing". <c>Tags</c> has no <c>UpdatedAt</c> column to stamp.
    /// A single-row update, so no transaction (design §9).
    /// </remarks>
    void Rename(TagId id, string name);

    /// <summary>
    /// <b>Hard-deletes</b> a tag and its <c>NoteTags</c> relationships, in
    /// <b>one transaction</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine's only hard delete (§8) — *"a tag is a label, not content"*.
    /// The row is removed, not flagged; there is no <c>DeletedAt</c> to set and
    /// no recycle bin for tags.
    /// </para>
    /// <para>
    /// <b>The notes themselves are never touched.</b> Only the join rows go
    /// (§11 row 21). Contract §10 lists this operation as atomic: "tag +
    /// NoteTags cascade".
    /// </para>
    /// </remarks>
    void Delete(TagId id);

    /// <summary>
    /// Whether this note already carries this tag.
    /// </summary>
    /// <remarks>
    /// The §7 idempotency decision depends on this: an assignment whose
    /// relationship already exists, and a removal whose relationship does not,
    /// both succeed <b>without writing</b>. The handler cannot honour that
    /// without being able to ask.
    /// </remarks>
    bool HasRelationship(NoteId noteId, TagId tagId);

    /// <summary>
    /// Creates the note–tag relationship.
    /// </summary>
    /// <remarks>
    /// <b>The note row is not changed</b> (contract §4): <c>NoteTags</c> has no
    /// timestamps and tagging a note does not modify the note. The caller has
    /// established that the note is active, the tag exists, and the
    /// relationship is absent.
    /// </remarks>
    void Assign(NoteId noteId, TagId tagId);

    /// <summary>
    /// Deletes the note–tag relationship.
    /// </summary>
    /// <remarks>
    /// As with <see cref="Assign"/>, the note row is untouched. The caller has
    /// established that the note is active and the relationship exists.
    /// </remarks>
    void Remove(NoteId noteId, TagId tagId);

    /// <summary>
    /// Every tag, ordered by name (Q4).
    /// </summary>
    /// <returns>
    /// The tags in <c>Name COLLATE NOCASE ASC</c> order (contract §5a, U13a),
    /// or an empty list when none exist.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Every</b> tag: there is no active/deleted distinction to make, because
    /// <c>Tags</c> has no <c>DeletedAt</c> — <c>DeleteTag</c> is the engine's
    /// only hard delete (§8). I1 has nothing to filter here.
    /// </para>
    /// <para>
    /// The order is <b>total without a tie-break</b>: <c>Name</c> is unique
    /// case-insensitively (design §5, <c>UX_Tags_Name</c>), so no two tags can
    /// compare equal under the same collation.
    /// </para>
    /// </remarks>
    IReadOnlyList<Tag> ListAll();
}
