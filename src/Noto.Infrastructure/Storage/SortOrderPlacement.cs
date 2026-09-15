namespace Noto.Infrastructure.Storage;

/// <summary>
/// Where a row should sit within its scope, expressed for
/// <see cref="SortOrderEngine"/>.
/// </summary>
/// <remarks>
/// The infrastructure-level twin of the domain's placement types. Ids are plain
/// strings here on purpose: the engine works over rows, and giving it
/// <c>NoteId</c> or <c>FolderId</c> would tie one algorithm to one domain. The
/// repositories translate at their own boundary, which is where the strong
/// types belong.
/// </remarks>
internal readonly record struct SortOrderPlacement
{
    private SortOrderPlacement(string? afterId, bool atEnd)
    {
        AfterId = afterId;
        AtEnd = atEnd;
    }

    /// <summary>The sibling to sit immediately after, when there is one.</summary>
    public string? AfterId { get; }

    /// <summary>Whether the row goes to the end of the scope.</summary>
    public bool AtEnd { get; }

    /// <summary>First in the scope — O3's <c>min − 1</c>.</summary>
    public static SortOrderPlacement First { get; } = new(null, atEnd: false);

    /// <summary>Last in the scope — O3's <c>max + 1</c>.</summary>
    public static SortOrderPlacement Last { get; } = new(null, atEnd: true);

    /// <summary>Immediately after an active sibling in the same scope.</summary>
    public static SortOrderPlacement After(string siblingId) => new(siblingId, atEnd: false);
}
