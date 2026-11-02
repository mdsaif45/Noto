namespace Noto.Core.Folders;

/// <summary>
/// The folders notes were most recently moved to (B11; workspace-management.md §3): the first choices when moving.
/// </summary>
/// <remarks>
/// <para>
/// Most recent first, no duplicates, at most <see cref="Capacity"/>. Only a move's destination counts. Stored as the
/// ids in order, comma-separated; anything that is not exactly that reads as empty rather than as a partial list.
/// </para>
/// <para>
/// A folder that has since been binned or purged is not removed here: <see cref="Shown"/> skips it, and it falls
/// out the next time the list is written.
/// </para>
/// </remarks>
public static class RecentFolders
{
    /// <summary>How many are kept.</summary>
    public const int Capacity = 5;

    private const char Separator = ',';

    /// <summary>The list after a note was moved to <paramref name="folder"/>: it first, the rest after, still at most <see cref="Capacity"/>.</summary>
    public static IReadOnlyList<FolderId> Push(IReadOnlyList<FolderId> recent, FolderId folder)
    {
        ArgumentNullException.ThrowIfNull(recent);

        return [folder, .. recent.Where(f => f != folder).Take(Capacity - 1)];
    }

    /// <summary>The stored form.</summary>
    public static string Format(IReadOnlyList<FolderId> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        return string.Join(Separator, recent.Select(f => f.Value));
    }

    /// <summary>The list a stored value holds; empty for an empty value and for anything malformed.</summary>
    public static IReadOnlyList<FolderId> Parse(string? stored) =>
        IsValid(stored) && stored!.Length > 0 ? [.. stored.Split(Separator).Select(FolderId.From)] : [];

    /// <summary>
    /// Whether a stored value is a list this type could have written: empty, or up to <see cref="Capacity"/> distinct
    /// ids separated by commas.
    /// </summary>
    public static bool IsValid(string? stored)
    {
        if (stored is null)
        {
            return false;
        }

        if (stored.Length == 0)
        {
            return true;
        }

        string[] ids = stored.Split(Separator);

        return ids.Length <= Capacity
            && ids.All(FolderId.IsValid)
            && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length;
    }

    /// <summary>
    /// What the move menu offers: the recent folders that are still among <paramref name="active"/>, most recent first,
    /// without <paramref name="excluding"/> (the note's own folder: a move there is not a move).
    /// </summary>
    public static IReadOnlyList<Folder> Shown(IReadOnlyList<FolderId> recent, IReadOnlyList<Folder> active, FolderId? excluding)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(active);

        return
        [
            .. recent
                .Where(id => id != excluding)
                .Select(id => active.FirstOrDefault(f => f.Id == id))
                .OfType<Folder>(),
        ];
    }
}
