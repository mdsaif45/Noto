namespace Noto.Core.Ordering;

/// <summary>Where an item is asked to move within its scope (B8, B9, C9; workspace-management.md, "B3 precisely").</summary>
public enum RepositionDirection
{
    Up,
    Down,
    Top,
    Bottom,
}

/// <summary>
/// The engine placement a move asks for: <see cref="Moves"/> false when the item is already where the move would put
/// it; otherwise after <see cref="After"/>, or first when that is <see langword="null"/>.
/// </summary>
public readonly record struct RepositionTarget<TId>(bool Moves, TId? After)
    where TId : struct;

/// <summary>
/// Turns "up / down / top / bottom within my pin group" into the engine's only placement, <c>After(sibling)</c> or
/// first, over the one SortOrder that pinned and unpinned items share.
/// </summary>
/// <remarks>
/// <para>
/// Display is O2: pinned first, then SortOrder, then Id. A move never crosses the pin boundary: the target is always
/// in the item's own group. Landing before a target means landing just after the target's predecessor in SortOrder
/// order, whatever group that predecessor is in; no item of the moving item's group lies between them.
/// </para>
/// <para>
/// Pure: the caller passes the scope as it is displayed, and dispatches the engine's reorder only when the result
/// moves.
/// </para>
/// </remarks>
public static class Reposition
{
    /// <summary>The placement for moving <paramref name="item"/> in <paramref name="direction"/>.</summary>
    /// <param name="displayed">The scope in display order (O2).</param>
    /// <param name="item">The item to move.</param>
    /// <param name="direction">Where to.</param>
    /// <param name="id">An item's id.</param>
    /// <param name="isPinned">Whether an item is pinned.</param>
    /// <param name="sortOrder">An item's SortOrder.</param>
    public static RepositionTarget<TId> Of<T, TId>(
        IReadOnlyList<T> displayed,
        TId item,
        RepositionDirection direction,
        Func<T, TId> id,
        Func<T, bool> isPinned,
        Func<T, double> sortOrder)
        where T : class
        where TId : struct, IEquatable<TId>
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(isPinned);
        ArgumentNullException.ThrowIfNull(sortOrder);

        int index = -1;
        for (int i = 0; i < displayed.Count; i++)
        {
            if (id(displayed[i]).Equals(item))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return new RepositionTarget<TId>(false, null);
        }

        bool pinned = isPinned(displayed[index]);
        List<T> group = [.. displayed.Where(x => isPinned(x) == pinned)];
        int position = group.FindIndex(x => id(x).Equals(item));

        switch (direction)
        {
            case RepositionDirection.Up when position > 0:
                return Before(group[position - 1], displayed, id, sortOrder);

            case RepositionDirection.Down when position < group.Count - 1:
                return new RepositionTarget<TId>(true, id(group[position + 1]));

            case RepositionDirection.Top when position > 0:
                return Before(group[0], displayed, id, sortOrder);

            case RepositionDirection.Bottom when position < group.Count - 1:
                return new RepositionTarget<TId>(true, id(group[^1]));

            default:
                return new RepositionTarget<TId>(false, null);
        }
    }

    /// <summary>
    /// Just before <paramref name="target"/>: after its SortOrder predecessor, or first. Neither the target nor the
    /// moving item can be that predecessor: the target is not below itself, and the moving item comes after the target
    /// in its own group, so its (SortOrder, Id) is above the target's.
    /// </summary>
    private static RepositionTarget<TId> Before<T, TId>(T target, IReadOnlyList<T> displayed, Func<T, TId> id, Func<T, double> sortOrder)
        where T : class
        where TId : struct, IEquatable<TId>
    {
        double anchor = sortOrder(target);
        string anchorId = id(target).ToString() ?? string.Empty;

        T? predecessor = displayed
            .Where(x => sortOrder(x) < anchor || (sortOrder(x) == anchor && string.CompareOrdinal(id(x).ToString(), anchorId) < 0))
            .OrderBy(sortOrder)
            .ThenBy(x => id(x).ToString(), StringComparer.Ordinal)
            .LastOrDefault();

        return predecessor is null
            ? new RepositionTarget<TId>(true, null)
            : new RepositionTarget<TId>(true, id(predecessor));
    }
}
