using Noto.Core.Notes;
using Noto.Core.Tags;

namespace Noto.UseCases.Tags;

/// <summary>
/// Q4 — every tag, ordered by name (contract §11 Q4, §5a U13a).
/// </summary>
/// <remarks>
/// <c>Name COLLATE NOCASE ASC</c>. Tags have no lifecycle — <c>Tags</c> has no
/// <c>DeletedAt</c>, because <c>DeleteTag</c> is the engine's only hard delete
/// (§8) — so there is nothing for I1 to filter and no "active" qualifier to
/// make. No pagination and no filtering: the contract specifies neither.
/// </remarks>
public sealed class ListTagsQuery(ITagRepository tags)
{
    private readonly ITagRepository _tags = tags
        ?? throw new ArgumentNullException(nameof(tags));

    public IReadOnlyList<Tag> Execute() => _tags.ListAll();
}

/// <summary>
/// Q5 — the active notes carrying a tag, most recently updated first
/// (contract §11 Q5, §5a U13b).
/// </summary>
/// <remarks>
/// <para>
/// <c>UpdatedAt DESC</c>, and deliberately <b>not</b> <c>SortOrder</c>: these
/// notes span folders, and <c>SortOrder</c> is scoped per folder (O1), so the
/// values cannot be compared across the result.
/// </para>
/// <para>
/// A tag that does not exist simply has no relationships, so the result is
/// empty rather than a failure — the same reasoning as U12b, where
/// <c>RemoveTagFromNote</c> does not validate the tag either.
/// </para>
/// <para>
/// Lives beside <see cref="ListTagsQuery"/> rather than with the note queries
/// because the tag is the subject: the caller has a tag and wants its notes.
/// </para>
/// </remarks>
public sealed class ListNotesForTagQuery(INoteRepository notes)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    public IReadOnlyList<Note> Execute(TagId tagId) => _notes.ListForTag(tagId);
}
