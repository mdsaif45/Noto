using Noto.Core.Folders;

namespace Noto.Core.Notes;

/// <summary>
/// A note — the product's central entity (core-note-engine-design.md §5).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Content"/> is markdown <b>source</b>, the only persisted form
/// (ADR-004). <see cref="Title"/> is computed from it, never stored.
/// </para>
/// <para>
/// The type carries no presentation state — no window, coordinate, monitor or
/// z-order — because a note may be presented several ways at once (ADR-009).
/// <see cref="IsFolded"/> is the deliberate exception argued in design §5: it
/// is user-authored note state that must persist identically in every surface,
/// not a rendering coordinate.
/// </para>
/// </remarks>
public sealed record Note
{
    public required NoteId Id { get; init; }

    /// <summary>Markdown source — the canonical form.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The first non-empty line of <see cref="Content"/>, heading markers
    /// removed (parity B16). Computed on every read: one source of truth.
    /// </summary>
    public string Title => NoteTitle.From(Content);

    /// <summary><see langword="null"/> means the root scope, not "no folder".</summary>
    public FolderId? FolderId { get; init; }

    /// <summary>A palette key, never a hex value — hex cannot follow a theme.</summary>
    public string? ColorKey { get; init; }

    public bool IsPinned { get; init; }

    public bool IsFolded { get; init; }

    /// <summary>
    /// Explicit order within the note's scope (design §7). Assigned by the
    /// engine; callers never supply it.
    /// </summary>
    public double SortOrder { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary><see langword="null"/> means live; set means in the recycle bin.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>Whether the note is live rather than in the recycle bin.</summary>
    public bool IsDeleted => DeletedAt is not null;
}
