namespace Noto.Core.Folders;

/// <summary>
/// A folder — a flat grouping of notes (core-note-engine-design.md §5).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately flat: no <c>ParentId</c>, no nesting, no tree. Parity C3 says
/// SideNotes' folders do not nest, and a hierarchy would complicate every
/// ordering and deletion rule for a capability the product does not have.
/// </para>
/// <para>
/// Introduced read-only in Slice 3, because <c>ListDeletedFolders</c> has to
/// return something. The folder <i>commands</i> — create, rename, delete,
/// restore, pin, reorder — are Slice 4 and are not implemented here.
/// </para>
/// </remarks>
public sealed record Folder
{
    public required FolderId Id { get; init; }

    public required string Name { get; init; }

    /// <summary>A palette key, never a hex value.</summary>
    public string? ColorKey { get; init; }

    public bool IsPinned { get; init; }

    /// <summary>
    /// Whether the folder is collapsed in the list.
    /// </summary>
    /// <remarks>
    /// User-authored state that must persist identically in every surface, not
    /// a rendering coordinate — the same argument that keeps
    /// <see cref="Notes.Note.IsFolded"/> on the domain type (design §5).
    /// </remarks>
    public bool IsCollapsed { get; init; }

    /// <summary>Explicit order among folders; the folder list is one scope.</summary>
    public double SortOrder { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary><see langword="null"/> means live; set means in the recycle bin.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>Whether the folder is in the recycle bin.</summary>
    public bool IsDeleted => DeletedAt is not null;
}
