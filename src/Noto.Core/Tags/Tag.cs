namespace Noto.Core.Tags;

/// <summary>
/// A tag — a label applied to notes (core-note-engine-design.md §5).
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>UpdatedAt</c> and no <c>DeletedAt</c>, deliberately.</b> Contract §4
/// lists <c>CreateTag</c>, <c>RenameTag</c> and <c>DeleteTag</c> as stamping
/// nothing, and §8 makes <c>DeleteTag</c> the engine's only hard delete — *"a
/// tag is a label, not content"*. Both rules are structural here rather than
/// conventions someone has to remember: the columns do not exist, so no
/// implementation can stamp or soft-delete a tag by accident.
/// </para>
/// <para>
/// <see cref="Name"/> is case-insensitively unique (design §5), enforced by
/// <c>UX_Tags_Name ... COLLATE NOCASE</c> in SchemaV1 and normalised per
/// contract §7a before it ever reaches the database.
/// </para>
/// </remarks>
public sealed record Tag
{
    public required TagId Id { get; init; }

    /// <summary>
    /// The tag's name, already trimmed (contract §7a).
    /// </summary>
    /// <remarks>
    /// The value stored is the normalised one. Leading and trailing whitespace
    /// is removed before validation, before the uniqueness comparison and
    /// before persistence; internal whitespace is never touched.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>A palette key, never a hex value.</summary>
    /// <remarks>
    /// The column exists in SchemaV1 and round-trips, but <b>no command sets
    /// it</b> in this slice — the same position as <c>Folder.IsCollapsed</c>.
    /// </remarks>
    public string? ColorKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
