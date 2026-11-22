namespace Noto.Core.Folders;

/// <summary>
/// The folder chooser a note from a global shortcut asks with, when Settings says to (#243; parity B6, J9): the
/// folders opened recently first, then every other folder, narrowed by what is typed.
/// </summary>
public static class FolderChoice
{
    /// <summary>
    /// The folders the chooser lists: the recently opened ones that still exist, most recent first, then the rest in
    /// folder-list order (<paramref name="active"/>'s), each once.
    /// </summary>
    public static IReadOnlyList<Folder> Order(IReadOnlyList<FolderId> recent, IReadOnlyList<Folder> active)
    {
        ArgumentNullException.ThrowIfNull(active);

        IReadOnlyList<Folder> first = RecentFolders.Shown(recent, active, excluding: null);
        return [.. first, .. active.Where(f => first.All(r => r.Id != f.Id))];
    }

    /// <summary>
    /// The folders whose name contains <paramref name="query"/>, ignoring case and the query's outer spaces, in the
    /// order given. An empty query keeps them all.
    /// </summary>
    public static IReadOnlyList<Folder> Filter(IReadOnlyList<Folder> ordered, string? query)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        string wanted = query?.Trim() ?? string.Empty;
        return wanted.Length == 0 ? ordered : [.. ordered.Where(f => f.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))];
    }
}
