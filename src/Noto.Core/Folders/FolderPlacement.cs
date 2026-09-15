namespace Noto.Core.Folders;

/// <summary>
/// Where a folder should sit within the folder collection.
/// </summary>
/// <remarks>
/// <para>
/// An intent, not a number — the same shape as <see cref="Notes.NotePlacement"/>
/// and for the same reason: <c>SortOrder</c> is computed by the ordering engine
/// and never supplied by the caller (contract §5).
/// </para>
/// <para>
/// A separate type from <c>NotePlacement</c> rather than a shared generic one,
/// because it carries a <see cref="FolderId"/>. The two exist so notes and
/// folders cannot be interchanged at the command boundary; collapsing them here
/// would reintroduce exactly the confusion the distinct id types prevent. The
/// shared algorithm lives one layer down, in the ordering engine, which works
/// over plain row ids.
/// </para>
/// </remarks>
public readonly record struct FolderPlacement
{
    private FolderPlacement(FolderId? afterSibling, bool atEnd)
    {
        AfterSibling = afterSibling;
        AtEnd = atEnd;
    }

    /// <summary>The sibling to sit immediately after, when there is one.</summary>
    public FolderId? AfterSibling { get; }

    /// <summary>Whether the folder goes to the end of the collection.</summary>
    public bool AtEnd { get; }

    /// <summary>First in the collection — O3's <c>min − 1</c>.</summary>
    public static FolderPlacement First { get; } = new(null, atEnd: false);

    /// <summary>Last in the collection — O3's <c>max + 1</c>.</summary>
    public static FolderPlacement Last { get; } = new(null, atEnd: true);

    /// <summary>Immediately after an active sibling.</summary>
    public static FolderPlacement After(FolderId sibling) => new(sibling, atEnd: false);
}
